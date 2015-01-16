#r "packages/FAKE/tools/FakeLib.dll"

open Fake

// Values
let solutionFile = "SQLiteMQ.sln"
let testDlls = !!"src/**/bin/Release/*Tests.dll"

let nunitRunner = 
  if getMachineEnvironment().Is64bit then ""
  else "-x86"
  |> sprintf "nunit-console%s.exe"

let sqliteInterop = 
  if getMachineEnvironment().Is64bit then "x64"
  else "x86"
  |> sprintf @"packages\System.Data.SQLite.Core\build\net451\%s\SQLite.Interop.dll"

//Copy SQLite.Interop
sqliteInterop |> CopyFile @"src\SQLiteMQ\SQLite.Interop.dll"
//Targets
Target "Build" (fun _ -> 
  !!solutionFile
  |> MSBuildRelease "" "Build"
  |> ignore)
Target "Test" (fun _ -> 
  testDlls |> NUnit(fun p -> 
                { p with DisableShadowCopy = true
                         ToolName = nunitRunner 
                         OutputFile = "TestResults.xml" }))
Target "Default" (fun _ -> trace "Building and Running Tests")
// Dependencies
"Build" ==> "Test" ==> "Default"
// start build
RunTargetOrDefault "Default"
