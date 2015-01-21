module MessageQueue

open Nessos.FsPickler
open System
open System.Data.SQLite
open System.IO

type Message<'a> = 
  { Id : int64
    Body : 'a }

type Operations = 
  inherit IDisposable
  abstract Enqueue<'a when 'a : not struct> : 'a -> unit
  abstract Dequeue<'a when 'a : not struct> : unit -> 'a option
  abstract DequeueAll<'a when 'a : not struct> : unit -> 'a seq
  abstract Delete<'a when 'a : not struct> : unit -> int
  abstract DeleteAll : unit -> int
  abstract PeekFirst<'a when 'a : not struct> : unit -> 'a option
  abstract PeekAll<'a when 'a : not struct> : unit -> 'a seq

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

let private pickler = FsPickler.CreateBinary()
let mutable private connection = Unchecked.defaultof<_>

let addParameter (name : string, value : obj) (command : SQLiteCommand) = 
  SQLiteParameter(name, value)
  |> command.Parameters.Add
  |> ignore
  command

let private addTypeParameter<'a> (command : SQLiteCommand) = command |> addParameter ("Type", typeof<'a>.FullName)
let private executeNonQuery (command : SQLiteCommand) = command.ExecuteNonQuery()
let private executeReader (command : SQLiteCommand) = command.ExecuteReader()

let asMessage<'a> (reader : SQLiteDataReader) = 
  use ms = new MemoryStream()
  reader.GetStream(1).CopyTo ms
  { Id = reader.GetInt64(0)
    Body = pickler.UnPickle<'a>(ms.ToArray()) }

let body = 
  function 
  | Some m -> Some m.Body
  | _ -> None

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
  |> ignore

let private deleteMessage message createCommand = 
  if createCommand DeleteWithId
     |> addParameter ("Id", message.Id)
     |> executeNonQuery
     <> 1
  then DeleteMessageFailed message.Id |> raise

let peek<'a> createCommand = 
  let reader = 
    createCommand SelectFirstOfType
    |> addTypeParameter<'a>
    |> executeReader
  if reader.Read() then 
    reader
    |> asMessage<'a>
    |> Some
  else None

let private peekAll<'a> createCommand = 
  let reader = 
    createCommand SelectAllOfType
    |> addTypeParameter<'a>
    |> executeReader
  seq { 
    while reader.Read() do
      yield (reader |> asMessage<'a>).Body
  }

let private dequeue<'a> createCommand deleteMessage = 
  peek<'a> createCommand |> Option.bind (fun message -> 
                              deleteMessage message createCommand
                              Some message.Body)

let private dequeueAll dequeue =
  Seq.initInfinite (fun _ -> dequeue()) |> Seq.takeWhile Option.isSome |> Seq.choose id

let private createTable _ = 
  use command = createCommand TableExists
  if not <| command.ExecuteReader().HasRows then 
    use command = createCommand CreateTable
    command
    |> executeNonQuery
    |> ignore

let deleteDefaultDatabase _ = IO.File.Delete "SQLiteMQ.db"

let create storage = 
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
