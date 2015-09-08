module MessageQueue

open Nessos.FsPickler
open System
open System.Data.SQLite
open System.IO
open System.Data.Common
open FSharp.Control

type Message<'a> = 
  { Id : int64
    Body : 'a }

type Operations = 
  inherit IDisposable
  abstract Enqueue<'a when 'a : not struct> : 'a -> Async<unit>
  abstract Dequeue<'a when 'a : not struct> : unit -> Async<'a option>
  abstract DequeueAll<'a when 'a : not struct> : unit -> AsyncSeq<'a>
  abstract Delete<'a when 'a : not struct> : unit -> Async<int>
  abstract DeleteAll : unit -> Async<int>
  abstract PeekFirst<'a when 'a : not struct> : unit -> Async<'a option>
  abstract PeekAll<'a when 'a : not struct> : unit -> AsyncSeq<'a>

type Storage = 
  | InMemory
  | DefaultFile
  | File of string

type Command = 
  | InsertOneOfType
  | DeleteWithId
  | SelectFirstOfType
  | SelectAllOfType
  | DeleteAllOfType
  | TableExists
  | CreateTable
  | DeleteAll

exception DeleteMessageFailed of int64

let private pickler = FsPickler.CreateBinarySerializer()
let mutable private connection = Unchecked.defaultof<_>

let addParameter (name : string, value : obj) (command : SQLiteCommand) = 
  SQLiteParameter(name, value)
  |> command.Parameters.Add
  |> ignore
  command

let private addTypeParameter<'a> (command : SQLiteCommand) = command |> addParameter ("Type", typeof<'a>.FullName)
let private executeNonQuery (command : SQLiteCommand) = command.ExecuteNonQueryAsync() |> Async.AwaitTask
let private executeReader (command : SQLiteCommand) = command.ExecuteReaderAsync() |> Async.AwaitTask
let private read (reader : DbDataReader) = reader.ReadAsync() |> Async.AwaitTask

let asMessage<'a> (reader : DbDataReader) = 
  use ms = new MemoryStream()
  reader.GetStream(1).CopyTo ms
  { Id = reader.GetInt64(0)
    Body = pickler.UnPickle<'a>(ms.ToArray()) }

let body message = 
  async { 
    let! m = message
    match m with
    | Some m -> return Some m.Body
    | _ -> return None
  }

let private createCommand command = 
  let sql = 
    match command with
    | InsertOneOfType -> "INSERT INTO MessageQueue(Type,Value) values (@Type,@Value)"
    | DeleteWithId -> "DELETE FROM MessageQueue WHERE Id = @Id"
    | DeleteAllOfType -> "DELETE FROM MessageQueue WHERE Type=@Type"
    | SelectFirstOfType -> "SELECT Id, Value FROM MessageQueue WHERE Type=@Type LIMIT 1"
    | SelectAllOfType -> "SELECT Id, Value FROM MessageQueue WHERE Type=@Type"
    | TableExists -> "SELECT name FROM sqlite_master WHERE type='table'"
    | CreateTable -> "CREATE TABLE MessageQueue (Id INTEGER PRIMARY KEY AUTOINCREMENT, Type VARCHAR, Value VARBINARY)"
    | DeleteAll -> "DELETE FROM MessageQueue"
  new SQLiteCommand(sql, connection)

let private enqueue<'a when 'a : not struct> o createCommand = 
  let pickle = pickler.Pickle<'a> o
  createCommand InsertOneOfType
  |> addTypeParameter<'a>
  |> addParameter ("Value", pickle)
  |> executeNonQuery
  |> Async.Ignore

let private deleteMessage message createCommand = 
  async { 
    let! result = createCommand DeleteWithId
                  |> addParameter ("Id", message.Id)
                  |> executeNonQuery
    match result with
    | 1 -> ()
    | _ -> DeleteMessageFailed message.Id |> raise
  }

let peek<'a> createCommand = 
  async { 
    let! reader = createCommand SelectFirstOfType
                  |> addTypeParameter<'a>
                  |> executeReader
    let! result = reader |> read
    return match result with
           | true -> 
             reader
             |> asMessage<'a>
             |> Some
           | _ -> None
  }

let private peekAll<'a> createCommand = 
  let reader = 
    createCommand SelectAllOfType
    |> addTypeParameter<'a>
    |> executeReader
  AsyncSeq.initInfiniteAsync (fun _ -> reader)
  |> AsyncSeq.takeWhileAsync read
  |> AsyncSeq.map (fun reader -> (reader |> asMessage<'a>).Body)

let private dequeue<'a> createCommand deleteMessage = 
  async { 
    let! message = peek<'a> createCommand
    match message with
    | Some message -> 
      do! deleteMessage message createCommand
      return Some message.Body
    | None -> return None
  }

let private dequeueAll dequeue = 
  AsyncSeq.initInfiniteAsync (fun _ -> dequeue())
  |> AsyncSeq.takeWhile Option.isSome
  |> AsyncSeq.choose id

let private createTable _ = 
  use command = createCommand TableExists
  if not <| command.ExecuteReader().HasRows then 
    use command = createCommand CreateTable
    command
    |> executeNonQuery
    |> ignore

let deleteDefaultDatabase() = File.Delete "SQLiteMQ.db"

let writeInterop() = 
  let ms = new MemoryStream()
  (System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("SQLite.Interop.dll")).CopyTo ms
  let path = Path.Combine(Environment.CurrentDirectory, "SQLite.Interop.dll")
  if not <| File.Exists path then File.WriteAllBytes(path, ms.ToArray())

let create storage = 
  writeInterop()
  connection <- match storage with
                | InMemory -> "Data Source=:memory:"
                | DefaultFile -> "Data Source=SQLiteMQ.db"
                | File location -> sprintf @"Data Source=%s" location
                |> fun cs -> new SQLiteConnection(cs)
  connection.Open()
  createTable connection
  { new Operations with
      member __.Enqueue o = enqueue o createCommand
      member __.Dequeue() = dequeue createCommand deleteMessage
      member this.DequeueAll() = dequeueAll this.Dequeue
      
      member __.Delete<'a when 'a : not struct>() = 
        createCommand DeleteAllOfType
        |> addTypeParameter<'a>
        |> executeNonQuery
      
      member __.DeleteAll() = createCommand DeleteAll |> executeNonQuery
      member __.PeekFirst() = peek createCommand |> body
      member __.PeekAll() = peekAll createCommand
    interface IDisposable with
      member __.Dispose() = connection.Dispose() }
