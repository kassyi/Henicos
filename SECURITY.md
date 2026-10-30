# Security Policy

## Reporting a vulnerability

Please do not open a public issue for a security problem. Report it privately through
[GitHub private vulnerability reporting](https://github.com/kassyi/Henicos/security/advisories/new)
(the "Report a vulnerability" button on the Security tab).

Examples of what counts:

- Generated CUDA or C++ code that reads or writes out of bounds where C# would have thrown
- Marshaling that corrupts host or device memory
- A build step or tool in this repository that runs code it should not

Include the G# commit, a minimal kernel or project that shows the problem, and the output of `just env-info`
(it leaves out user names, host names and serial numbers).

## What happens next

The report stays visible only to you and the maintainer. You get a first reply within a week. The fix is prepared in
a private fork, and the advisory is published, with credit to you unless you ask otherwise, once the fix is on `main`.

## Supported versions

G# has no release yet. Only the latest `main` receives fixes.
