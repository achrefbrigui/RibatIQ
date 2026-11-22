# Test fixtures

Inputs for RibatIQ integration tests. They are copied to the test output and analyzed, never built.

`SampleSolution` contains **intentional defects** that tests assert on (empty catch, `throw ex;`,
async void, a fake hard-coded password, a syntax error in a nested project not listed in the
solution). The password is a fixture value, not a real secret. `SampleSolution.sln` also lists a
missing project to exercise discovery warnings.

`AiSample` contains defects that **only the AI review** can find (SQL injection, null dereference,
N+1 queries, data race...), with the expected rule per line in its README. Static analysis finds
nothing there (a test checks it). Use it to check a provider by hand.
