using System.Runtime.CompilerServices;

// Internal native lifecycle modules may be exercised by the offline test
// assembly without making a live installer entry point public.
[assembly: InternalsVisibleTo("Finesse.Compatibility.OfflineTests")]

// Internal lifecycle is consumed only by the new source-built Finesse.exe host.
// No general public driver-install API or live command tool is exposed.
[assembly: InternalsVisibleTo("Finesse")]
