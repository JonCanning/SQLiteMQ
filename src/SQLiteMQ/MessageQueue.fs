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
  abstract Clear<'a when 'a : not struct> : unit -> int
  abstract ClearAll : unit -> int
  abstract Peek<'a when 'a : not struct> : unit -> 'a option
  abstract Delete<'a when 'a : not struct> : unit -> unit

type Storage = 
  | InMemory
  | File of string

type Command = 
  | InsertOneOfType
  | DeleteOneOfType
  | SelectOneOfType
  | SelectAllOfType
  | DeleteAllOfType
  | TableExists
  | CreateTable
  | ClearAll

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
    | DeleteOneOfType -> 
      "DELETE FROM MessageQueue WHERE ROWID = (SELECT ROWID FROM MessageQueue WHERE Type=@Type LIMIT 1)"
    | DeleteAllOfType -> "DELETE FROM MessageQueue WHERE Type=@Type"
    | SelectOneOfType -> "SELECT Value FROM MessageQueue WHERE Type=@Type LIMIT 1"
    | SelectAllOfType -> "SELECT Value FROM MessageQueue WHERE Type=@Type"
    | TableExists -> "SELECT name FROM sqlite_master WHERE type='table'"
    | CreateTable -> "CREATE TABLE MessageQueue (Type VARCHAR, Value VARBINARY)"
    | ClearAll -> "DELETE FROM MessageQueue"
  new SQLiteCommand(sql, connection)

let private enqueue<'a> o command = 
  let pickle = pickler.Pickle<'a> o
  command
  |> addTypeParameter<'a>
  |> addParameter ("Value", pickle)
  |> executeNonQuery
  |> ignore

let private delete<'a> command = 
  if command
     |> addTypeParameter<'a>
     |> executeNonQuery
     <> 1
  then DeleteObjectFailed typeof<'a>.FullName |> raise

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
  if o.IsSome then createCommand DeleteOneOfType |> delete<'a>
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
      createCommand DeleteOneOfType |> delete<'a>
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
                | File location -> sprintf @"Data Source=%s\SQLiteMQ.db" location
                |> fun cs -> new SQLiteConnection(cs)
  connection.Open()
  createTable connection
  { new Operations with
      member __.Enqueue o = createCommand InsertOneOfType |> enqueue o
      member __.Dequeue() = createCommand SelectOneOfType |> dequeue
      member __.DequeueAll() = createCommand SelectAllOfType |> dequeueAll
      
      member __.Clear<'a when 'a : not struct>() = 
        createCommand DeleteAllOfType
        |> addTypeParameter<'a>
        |> executeNonQuery
      
      member __.ClearAll() = createCommand ClearAll |> executeNonQuery
      member __.Peek() = createCommand SelectOneOfType |> peek
      member __.Delete<'a when 'a : not struct>() = createCommand DeleteOneOfType |> delete<'a>
    interface IDisposable with
      member __.Dispose() = connection.Dispose() }
