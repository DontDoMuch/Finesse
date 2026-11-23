using System.Runtime.CompilerServices;

// Internal native lifecycle modules may be exercised by the offline test
// assembly without making a live installer entry point public.
[assembly: InternalsVisibleTo("Finesse.Compatibility.OfflineTests")]
