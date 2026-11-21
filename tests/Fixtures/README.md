# Test fixtures

Inputs for RibatIQ integration tests. They are copied to the test output and analyzed, never built.

`SampleSolution` contains **intentional defects** that tests assert on (empty catch, `throw ex;`,
async void, a fake hard-coded password, a syntax error in a nested project not listed in the
solution). The password is a fixture value, not a real secret. `SampleSolution.sln` also lists a
missing project to exercise discovery warnings.
