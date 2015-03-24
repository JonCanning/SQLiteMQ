[<AutoOpen>]
module TestHelpers

open NUnit.Framework

let (==) actual expected = Assert.AreEqual(box expected, box actual)
let (!=) actual expected = Assert.AreNotEqual(box expected, box actual)

type Test = TestAttribute
type TestCase = TestCaseAttribute
type SetUp = TestFixtureSetUpAttribute