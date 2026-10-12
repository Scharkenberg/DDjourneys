//
// Vendored from QRCoder (https://github.com/codebude/QRCoder), tag v1.5.1,
// file QRCoder/QRCodeData.cs. Copyright (c) 2013-2018 Raffael Herrmann, MIT
// licence (see LICENSE.txt in the upstream repository).
// Vendored 2026-10-12 for the share-a-stop QR code (see QrCode).
// Stripped: the raw-data persistence surface of QRCoder's own .qrr files (the
// two constructors from a path/bytes, GetRawData, SaveRawData and the Compression
// enum) - it is the only part that needs System.IO.Compression and the
// Framework4.0 shims, which are not vendored; the app reads the module matrix.
// The using directives those members needed went with them; Dispose carries a
// scoped #pragma warning disable CS8625 (upstream code predates nullable
// annotations; this repository builds with zero warnings). Everything else is
// byte-identical to the tag, including the upstream 4-space indentation - this
// file keeps the upstream shape on purpose; do not re-indent it to the
// repository's tabs. Style messages about the upstream shape (namespace, the
// Dispose pattern) are suppressed for the whole file.
//
#pragma warning disable IDE0130 // Namespace does not match folder structure
#pragma warning disable CA1816 // Dispose methods should call GC.SuppressFinalize

using System.Collections;
using System.Collections.Generic;

namespace QRCoder
{
    using System;

    public class QRCodeData : IDisposable
    {
        public List<BitArray> ModuleMatrix { get; set; }

        public QRCodeData(int version)
        {
            this.Version = version;
            var size = ModulesPerSideFromVersion(version);
            this.ModuleMatrix = new List<BitArray>();
            for (var i = 0; i < size; i++)
                this.ModuleMatrix.Add(new BitArray(size));
        }

        public int Version { get; private set; }

        private static int ModulesPerSideFromVersion(int version)
        {
            return 21 + (version - 1) * 4;
        }

            // The pragma: upstream code predates nullable annotations; this repository builds with zero warnings.
#pragma warning disable CS8625
        public void Dispose()
        {
            this.ModuleMatrix = null;
            this.Version = 0;

        }
#pragma warning restore CS8625

    }
}
