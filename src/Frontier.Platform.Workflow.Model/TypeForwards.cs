using System.Runtime.CompilerServices;
using Frontier.Platform.Workflow.Model;

// ADR-PA30: the ADR-E2 envelope moved to Frontier.Platform.Serialization so the governance tier
// (Audit) can carry it without depending on the engine. The full type names are unchanged, so an
// assembly compiled against this package still binds through these forwards, and source that
// references this package still compiles because it references Serialization transitively.
[assembly: TypeForwardedTo(typeof(TypedPayload))]
[assembly: TypeForwardedTo(typeof(PayloadRef))]
