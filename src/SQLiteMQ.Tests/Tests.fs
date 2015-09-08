module Tests

open MessageQueue
open FSharp.Control

type Cat = 
  { Name : string }

type Dog = 
  { Name : string }

[<Test>]
let ``it enqueues and dequeues``() = 
  use mq = MessageQueue.create InMemory
  let cat = { Cat.Name = "Bubbles" }
  cat |> mq.Enqueue |> Async.RunSynchronously
  mq.Dequeue<Cat>() |> Async.RunSynchronously == Some cat
  mq.Dequeue<Cat>() |> Async.RunSynchronously == None

[<Test>]
let ``it dequeues FIFO``() = 
  use mq = MessageQueue.create InMemory
  let cats = [ 1..10 ] |> List.map (fun i -> { Cat.Name = sprintf "%i" i })
  cats |> Seq.map mq.Enqueue |> Async.Parallel |> Async.RunSynchronously |> ignore
  for cat in cats do
    mq.Dequeue<Cat>() |> Async.RunSynchronously == Some cat

[<Test>]
let ``it queues different types``() = 
  use mq = MessageQueue.create InMemory
  let cat = { Cat.Name = "Bubbles" }
  let dog = { Dog.Name = "Rufus" }
  cat |> mq.Enqueue |> Async.RunSynchronously
  dog |> mq.Enqueue |> Async.RunSynchronously
  mq.Dequeue<Dog>() |> Async.RunSynchronously == Some dog
  mq.Dequeue<Cat>() |> Async.RunSynchronously == Some cat

[<Test>]
let ``it can clear the queue of a type``() = 
  use mq = MessageQueue.create InMemory
  let cats = [ 1..10 ] |> List.map (fun i -> { Cat.Name = sprintf "%i" i })
  cats |> Seq.map mq.Enqueue |> Async.Parallel |> Async.RunSynchronously |> ignore
  let dog = { Dog.Name = "Rufus" }
  dog |> mq.Enqueue |> Async.RunSynchronously
  mq.Delete<Cat>() |> Async.RunSynchronously == 10
  mq.Dequeue<Cat>() |> Async.RunSynchronously == None
  mq.Dequeue<Dog>() |> Async.RunSynchronously == Some dog

[<Test>]
let ``it can clear the queue of all types``() = 
  use mq = MessageQueue.create InMemory
  { Cat.Name = "Bubbles" } |> mq.Enqueue |> Async.RunSynchronously
  { Dog.Name = "Rufus" } |> mq.Enqueue |> Async.RunSynchronously
  mq.DeleteAll() |> Async.RunSynchronously == 2
  mq.Dequeue<Cat>() |> Async.RunSynchronously == None
  mq.Dequeue<Dog>() |> Async.RunSynchronously == None

[<Test>]
let ``it can return a sequence of a type``() = 
  use mq = MessageQueue.create InMemory
  let cats = [ 1..10 ] |> List.map (fun i -> { Cat.Name = sprintf "%i" i })
  cats |> Seq.map mq.Enqueue |> Async.Parallel |> Async.RunSynchronously |> ignore
  mq.DequeueAll<Cat>() |> AsyncSeq.toList == cats
  mq.Dequeue<Cat>() |> Async.RunSynchronously == None

[<Test>]
let ``it persists the queue``() = 
  deleteDefaultDatabase()
  let createMq _ = MessageQueue.create DefaultFile
  use mq = createMq()
  let cat = { Cat.Name = "Bubbles" }
  cat |> mq.Enqueue |> Async.RunSynchronously
  use mq = createMq()
  let cat = { Cat.Name = "Bubbles" }
  mq.Dequeue<Cat>() |> Async.RunSynchronously == Some cat

[<Test>]
let ``it peeks 1``() = 
  use mq = MessageQueue.create InMemory
  let cat = { Cat.Name = "Bubbles" }
  cat |> mq.Enqueue |> Async.RunSynchronously
  mq.PeekFirst<Cat>() |> Async.RunSynchronously == Some cat
  mq.PeekFirst<Cat>() |> Async.RunSynchronously == Some cat

[<Test>]
let ``it peeks all``() = 
  use mq = MessageQueue.create InMemory
  let cats = [ 1..10 ] |> List.map (fun i -> { Cat.Name = sprintf "%i" i })
  cats |> Seq.map mq.Enqueue |> Async.Parallel |> Async.RunSynchronously |> ignore
  mq.PeekAll<Cat>() |> AsyncSeq.toList == cats
  mq.PeekAll<Cat>() |> AsyncSeq.toList == cats