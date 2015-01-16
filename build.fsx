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
Target "NuGet" (fun _ -> 
  (new System.Net.WebClient()).DownloadFile("http://nuget.org/nuget.exe", @"nuget.exe")
  directExec 
    (fun psi -> 
    psi.FileName <- "nuget.exe"
    psi.Arguments <- sprintf "pack src\SQLiteMQ\SQLiteMQ.fsproj -Version %s -Prop Configuration=Release" 
                       appVeyorBuildVersion)
  |> ignore
  directExec 
    (fun psi -> 
    psi.FileName <- "nuget.exe"
    psi.Arguments <- sprintf "push SQLiteMQ.%s.nupkg -ApiKey %s -Source %s" appVeyorBuildVersion 
                     <| environVar "NUGETAPIKEY" <| environVar "NUGETSOURCE")
  |> ignore)
Target "Default" (fun _ -> trace "Building and Running Tests")
Target "Appveyor" DoNothing
// Dependencies
"Build" ==> "Test" ==> "Default"
"Build" ==> "Test" ==> "Nuget" ==> "Appveyor"
// start build
RunTargetOrDefault "Default"
