#r "packages/FAKE/tools/FakeLib.dll"

open Fake

// Values
let solutionFile = "SQLiteMQ.sln"
let testDlls = !!"src/**/bin/Release/*Tests.dll"

//Targets
Target "Build" (fun _ -> 
  !!solutionFile
  |> MSBuildRelease "" "Build"
  |> ignore)
Target "Test" (fun _ -> 
  testDlls |> NUnit(fun p -> 
                { p with DisableShadowCopy = true
                         OutputFile = "TestResults.xml" })
  if appVeyorBuildVersion <> null then 
    AppVeyor.UploadTestResultsXml AppVeyor.TestResultsType.NUnit "/")
Target "NuGet" (fun _ ->
  Paket.Pack(fun p -> { p with Version = appVeyorBuildVersion })
  Paket.Push(fun p -> 
    { p with ApiKey = environVar "NUGETAPIKEY"
             PublishUrl = environVar "NUGETSOURCE"
             WorkingDir = "./temp" })
  |> ignore)
Target "Default" DoNothing
Target "Appveyor" DoNothing
// Dependencies
"Build" ==> "Test" ==> "Default"
"Build" ==> "Test" ==> "Nuget" ==> "Appveyor"
// start build
RunTargetOrDefault "Default"
