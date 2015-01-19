module MessageQueue

open Nessos.FsPickler
open System
open System.Data.SQLite
open System.IO

type Operations = 
  inherit IDisposable
  abstract Enqueue<'a when 'a : not struct> : 'a -> unit
  abstract Dequeue<'a when 'a : not struct> : unit -> 'a option
  abstract DequeueAll<'a when 'a : not struct> : unit -> 'a seq
  abstract Delete<'a when 'a : not struct> : unit -> int
  abstract DeleteAll : unit -> int
  abstract PeekFirst<'a when 'a : not struct> : unit -> 'a option
  abstract PeekAll<'a when 'a : not struct> : unit -> 'a seq
  abstract DeleteFirst<'a when 'a : not struct> : unit -> unit

type Storage = 
  | InMemory
  | DefaultFile
  | File of string

type Command = 
  | InsertOneOfType
  | DeleteFirstOfType
  | SelectFirstOfType
  | SelectAllOfType
  | DeleteAllOfType
  | TableExists
  | CreateTable
  | DeleteAll

exception DeleteObjectFailed of string

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

let private createCommand command = 
  let sql = 
    match command with
    | InsertOneOfType -> "INSERT INTO MessageQueue(Type,Value) values (@Type,@Value)"
    | DeleteFirstOfType -> 
      "DELETE FROM MessageQueue WHERE ROWID = (SELECT ROWID FROM MessageQueue WHERE Type=@Type LIMIT 1)"
    | DeleteAllOfType -> "DELETE FROM MessageQueue WHERE Type=@Type"
    | SelectFirstOfType -> "SELECT Value FROM MessageQueue WHERE Type=@Type LIMIT 1"
    | SelectAllOfType -> "SELECT Value FROM MessageQueue WHERE Type=@Type"
    | TableExists -> "SELECT name FROM sqlite_master WHERE type='table'"
    | CreateTable -> "CREATE TABLE MessageQueue (Type VARCHAR, Value VARBINARY)"
    | DeleteAll -> "DELETE FROM MessageQueue"
  new SQLiteCommand(sql, connection)

let private enqueue<'a> o command = 
  let pickle = pickler.Pickle<'a> o
  command
  |> addTypeParameter<'a>
  |> addParameter ("Value", pickle)
  |> executeNonQuery
  |> ignore

let private deleteFirstOfType<'a> command = 
  if command
     |> addTypeParameter<'a>
     |> executeNonQuery
     <> 1
  then DeleteObjectFailed typeof<'a>.FullName |> raise

let private peekAll<'a> command =
  let reader = 
    command
    |> addTypeParameter<'a>
    |> executeReader
  seq { 
    while reader.Read() do
      use ms = new MemoryStream()
      reader.GetStream(0).CopyTo ms
      yield pickler.UnPickle<'a>(ms.ToArray())
  }

let peek<'a> command = 
  let reader = 
    command
    |> addTypeParameter<'a>
    |> executeReader
  if reader.Read() then 
    use ms = new MemoryStream()
    reader.GetStream(0).CopyTo ms
    let o = pickler.UnPickle<'a>(ms.ToArray())
    Some o
  else None

let private dequeue<'a> command = 
  let o = peek<'a> command
  if o.IsSome then createCommand DeleteFirstOfType |> deleteFirstOfType<'a>
  o

let private dequeueAll<'a> command = 
  let reader = 
    command
    |> addTypeParameter<'a>
    |> executeReader
  seq { 
    while reader.Read() do
      use ms = new MemoryStream()
      reader.GetStream(0).CopyTo ms
      createCommand DeleteFirstOfType |> deleteFirstOfType<'a>
      yield pickler.UnPickle<'a>(ms.ToArray())
  }

let private createTable _ = 
  use command = createCommand TableExists
  if not <| command.ExecuteReader().HasRows then 
    use command = createCommand CreateTable
    command
    |> executeNonQuery
    |> ignore

let create storage = 
  connection <- match storage with
                | InMemory -> "Data Source=:memory:"
                | DefaultFile -> "Data Source=SQLiteMQ.db"
                | File location -> sprintf @"Data Source=%s" location
                |> fun cs -> new SQLiteConnection(cs)
  connection.Open()
  createTable connection
  { new Operations with
      member __.Enqueue o = createCommand InsertOneOfType |> enqueue o
      member __.Dequeue() = createCommand SelectFirstOfType |> dequeue
      member __.DequeueAll() = createCommand SelectAllOfType |> dequeueAll
      
      member __.Delete<'a when 'a : not struct>() = 
        createCommand DeleteAllOfType
        |> addTypeParameter<'a>
        |> executeNonQuery
      
      member __.DeleteAll() = createCommand DeleteAll |> executeNonQuery
      member __.PeekFirst() = createCommand SelectFirstOfType |> peek
      member __.PeekAll() = createCommand SelectAllOfType |> peekAll
      member __.DeleteFirst<'a when 'a : not struct>() = createCommand DeleteFirstOfType |> deleteFirstOfType<'a>
    interface IDisposable with
      member __.Dispose() = connection.Dispose() }
