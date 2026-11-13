// Tests read the internal reducer and receipt-edge tables. Workbench.Host gets no grant, so adapters cannot mint what only Activity may build.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Workbench.Core.Tests")]
