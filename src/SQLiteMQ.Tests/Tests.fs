module Tests

open MessageQueue

type Cat = 
  { Name : string }

type Dog = 
  { Name : string }

[<Test>]
let ``it enqueues and dequeues``() = 
  use mq = MessageQueue.create InMemory
  let cat = { Cat.Name = "Bubbles" }
  cat |> mq.Enqueue
  mq.Dequeue<Cat>() == Some cat
  mq.Dequeue<Cat>() == None

[<Test>]
let ``it dequeues FIFO``() = 
  use mq = MessageQueue.create InMemory
  let cats = [ 1..10 ] |> List.map (fun i -> { Cat.Name = sprintf "%i" i })
  cats |> Seq.iter mq.Enqueue
  for cat in cats do
    mq.Dequeue<Cat>() == Some cat

[<Test>]
let ``it queues different types``() = 
  use mq = MessageQueue.create InMemory
  let cat = { Cat.Name = "Bubbles" }
  let dog = { Dog.Name = "Rufus" }
  cat |> mq.Enqueue
  dog |> mq.Enqueue
  mq.Dequeue<Dog>() == Some dog
  mq.Dequeue<Cat>() == Some cat

[<Test>]
let ``it can clear the queue of a type``() = 
  use mq = MessageQueue.create InMemory
  let cats = [ 1..10 ] |> List.map (fun i -> { Cat.Name = sprintf "%i" i })
  cats |> Seq.iter mq.Enqueue
  let dog = { Dog.Name = "Rufus" }
  dog |> mq.Enqueue
  mq.Delete<Cat>() == 10
  mq.Dequeue<Cat>() == None
  mq.Dequeue<Dog>() == Some dog

[<Test>]
let ``it can clear the queue of all types``() = 
  use mq = MessageQueue.create InMemory
  { Cat.Name = "Bubbles" } |> mq.Enqueue
  { Dog.Name = "Rufus" } |> mq.Enqueue
  mq.DeleteAll() == 2
  mq.Dequeue<Cat>() == None
  mq.Dequeue<Dog>() == None

[<Test>]
let ``it can return a sequence of a type``() = 
  use mq = MessageQueue.create InMemory
  let cats = [ 1..10 ] |> List.map (fun i -> { Cat.Name = sprintf "%i" i })
  cats |> Seq.iter mq.Enqueue
  mq.DequeueAll<Cat>() == cats
  mq.Dequeue<Cat>() == None

[<Test>]
let ``it persists the queue``() = 
  deleteDefaultDatabase()
  let createMq _ = MessageQueue.create DefaultFile
  use mq = createMq()
  let cat = { Cat.Name = "Bubbles" }
  cat |> mq.Enqueue
  use mq = createMq()
  let cat = { Cat.Name = "Bubbles" }
  mq.Dequeue<Cat>() == Some cat

[<Test>]
let ``it peeks 1``() = 
  use mq = MessageQueue.create InMemory
  let cat = { Cat.Name = "Bubbles" }
  cat |> mq.Enqueue
  mq.PeekFirst<Cat>() == Some cat
  mq.PeekFirst<Cat>() == Some cat

[<Test>]
let ``it peeks all``() = 
  use mq = MessageQueue.create InMemory
  let cats = [ 1..10 ] |> List.map (fun i -> { Cat.Name = sprintf "%i" i })
  cats |> Seq.iter mq.Enqueue
  mq.PeekAll<Cat>() == cats
  mq.PeekAll<Cat>() == cats