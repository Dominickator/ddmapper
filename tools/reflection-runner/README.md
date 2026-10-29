# Reflection runner (sandbox workaround, not part of the test suite)

`dotnet test` cannot run in this sandbox: vstest.console launches testhost,
which connects, but the testhost↔console channel is then hijacked by the
sandbox egress proxy (see the Stage 1 test report for the diag-log evidence).
This runner executes the exact same [Fact] methods and xUnit asserts without
vstest: it loads DDMapper.Tests.dll and invokes every method carrying
`Xunit.FactAttribute` via reflection, printing PASS/FAIL per test and a
`RESULT passed=N failed=M` summary, exiting non-zero on any failure.

Run from this directory:

```
~/workspace/.dotnet/dotnet run
```

The ProjectReference uses an absolute path to the test project on this VM.
On a machine where `dotnet test` works, prefer `dotnet test` — this runner
exists only to give honest execution evidence where vstest cannot connect.
