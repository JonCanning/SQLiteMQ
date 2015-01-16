#r "packages/FAKE/tools/FakeLib.dll"

open Fake

// Values
let solutionFile = "SQLiteMQ.sln"
let testDlls = !!"src/**/bin/Release/*Tests.dll"

//Copy SQLite.Interop
@"packages\System.Data.SQLite.Core\build\net451\x64\SQLite.Interop.dll" |> CopyFile @"src\SQLiteMQ\SQLite.Interop.dll"
//Targets
Target "Build" (fun _ -> 
  !!solutionFile
  |> MSBuildRelease "" "Build"
  |> ignore)
Target "Test" (fun _ -> 
  testDlls |> NUnit(fun p -> 
                { p with DisableShadowCopy = true
                         OutputFile = "TestResults.xml" }))
Target "Default" (fun _ -> trace "Building and Running Tests")
// Dependencies
"Build" ==> "Test" ==> "Default"
// start build
RunTargetOrDefault "Default"
