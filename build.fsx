#r "packages/FAKE/tools/FakeLib.dll"

open Fake

// Values
let solutionFile = "SQLiteMQ.sln"
let testDlls = !!"src/**/bin/Release/*Tests.dll"

//Targets
Target "Build" (fun _ -> 
  let buildParams _ = 
    { MSBuildDefaults with Verbosity = Some Quiet
                           Properties = [ "Configuration", "Release" ] }
  build buildParams solutionFile)
Target "Test" (fun _ -> 
  testDlls |> NUnit(fun p -> 
                { p with DisableShadowCopy = true
                         OutputFile = "TestResults.xml" })
  if appVeyorBuildVersion <> null then AppVeyor.UploadTestResultsXml AppVeyor.TestResultsType.NUnit "/")
Target "NuGet" (fun _ -> 
  Paket.Pack(fun p -> { p with Version = appVeyorBuildVersion })
  let consoleOut = System.Console.Out
  System.IO.TextWriter.Null |> System.Console.SetOut
  Paket.Push(fun p -> 
    { p with ApiKey = environVar "NUGETAPIKEY"
             PublishUrl = environVar "NUGETSOURCE"
             WorkingDir = "./temp" })
  System.Console.SetOut consoleOut)
Target "Default" DoNothing
Target "Appveyor" DoNothing
// Dependencies
"Build" ==> "Test" ==> "Default"
"Build" ==> "Test" ==> "Nuget" ==> "Appveyor"
// start build
RunTargetOrDefault "Default"
