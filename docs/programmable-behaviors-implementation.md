# Authored application implementation record

The saved `IBehavior` and Roslyn `.csx` runtime described by the former version of this document has been removed.

The current implementation uses ordinary C# files compiled into immutable artifacts, a principal-partitioned `IApplicationAuthoring` service, durable application kernel state, and supervised worker processes. See the [file-based scripting design](superpowers/specs/2026-09-07-file-based-scripting-design.md), [getting started](GETTING_STARTED.md), and [recorded validation](programmable-behaviors-validation.md).

The design document includes capabilities that remain staged or bounded. Treat the validation record and executable acceptance tests as the source of current guarantees.
