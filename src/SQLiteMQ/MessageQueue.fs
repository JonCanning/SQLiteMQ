module MessageQueue

open Nessos.FsPickler
open System
open System.Data.SQLite
open System.Diagnostics
open System.IO

type Operations = 
  inherit IDisposable
  abstract Enqueue<'a when 'a : not struct> : 'a -> unit
  abstract Dequeue<'a when 'a : not struct> : unit -> 'a option
  abstract DequeueAll<'a when 'a : not struct> : unit -> 'a seq
  abstract Clear<'a when 'a : not struct> : unit -> int
  abstract ClearAll : unit -> int

type Storage = 
  | InMemory
  | File of string

type Command = 
  | Enqueue
  | Delete
  | Dequeue
  | DequeueAll
  | Clear
  | TableExists
  | CreateTable
  | ClearAll

exception DeleteObjectFailed of string

let private pickler = FsPickler.CreateBinary()
let mutable private connection = Unchecked.defaultof<_>

let private addType<'a> (command : SQLiteCommand) = 
  SQLiteParameter("Type", typeof<'a>.FullName)
  |> command.Parameters.Add
  |> ignore
  command

let private executeNonQuery (command : SQLiteCommand) = command.ExecuteNonQuery()
let private executeReader (command : SQLiteCommand) = command.ExecuteReader()

let private createCommand command = 
  let sql = 
    match command with
    | Enqueue -> "INSERT INTO MessageQueue(Type,Value) values (@Type,@Value)"
    | Delete -> "DELETE FROM MessageQueue WHERE ROWID = (SELECT ROWID FROM MessageQueue WHERE Type=@Type LIMIT 1)"
    | Clear -> "DELETE FROM MessageQueue WHERE Type=@Type"
    | Dequeue -> "SELECT Value FROM MessageQueue WHERE Type=@Type LIMIT 1"
    | DequeueAll -> "SELECT Value FROM MessageQueue WHERE Type=@Type"
    | TableExists -> "SELECT name FROM sqlite_master WHERE type='table'"
    | CreateTable -> "CREATE TABLE MessageQueue (Type VARCHAR, Value VARBINARY)"
    | ClearAll -> "DELETE FROM MessageQueue"
  new SQLiteCommand(sql, connection)

let private enqueue<'a> o command = 
  let pickle = pickler.Pickle<'a> o
  command
  |> addType<'a>
  |> fun c -> SQLiteParameter("Value", pickle) |> c.Parameters.Add
  |> ignore
  command.ExecuteNonQuery() |> ignore

let private delete<'a> command = 
  match command
        |> addType<'a>
        |> executeNonQuery with
  | 1 -> ()
  | _ -> DeleteObjectFailed typeof<'a>.FullName |> raise

let private dequeue<'a> command = 
  let reader = 
    command
    |> addType<'a>
    |> executeReader
  if reader.Read() then 
    use ms = new MemoryStream()
    reader.GetStream(0).CopyTo ms
    let o = pickler.UnPickle<'a>(ms.ToArray())
    createCommand Delete |> delete<'a>
    Some o
  else None

let private dequeueAll<'a> command = 
  let reader = 
    command
    |> addType<'a>
    |> executeReader
  seq { 
    while reader.Read() do
      use ms = new MemoryStream()
      reader.GetStream(0).CopyTo ms
      createCommand Delete |> delete<'a>
      yield pickler.UnPickle<'a>(ms.ToArray())
  }

let private clear<'a> command = 
  command
  |> addType<'a>
  |> executeNonQuery

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
      member __.Enqueue o = createCommand Enqueue |> enqueue o
      member __.Dequeue() = createCommand Dequeue |> dequeue
      member __.DequeueAll() = createCommand DequeueAll |> dequeueAll
      member __.Clear<'a when 'a : not struct>() = createCommand Clear |> clear<'a>
      member __.ClearAll() = createCommand ClearAll |> executeNonQuery
    interface IDisposable with
      member __.Dispose() = connection.Dispose() }
