//
// Vendored from QRCoder (https://github.com/codebude/QRCoder), tag v1.5.1,
// file QRCoder/Exceptions/DataTooLongException.cs. Copyright (c) 2013-2018
// Raffael Herrmann, MIT licence (see LICENSE.txt in the upstream repository).
// Vendored 2026-10-12 for the share-a-stop QR code (see QrCode). Byte-identical
// to the tag; only the byte-order mark was removed, to match this repository's
// files. The namespace-message is suppressed for the whole file (upstream namespace).
//
#pragma warning disable IDE0130 // Namespace does not match folder structure

using System;

namespace QRCoder.Exceptions
{
    public class DataTooLongException : Exception
    {
        public DataTooLongException(string eccLevel, string encodingMode, int maxSizeByte) : base(
            $"The given payload exceeds the maximum size of the QR code standard. The maximum size allowed for the choosen paramters (ECC level={eccLevel}, EncodingMode={encodingMode}) is {maxSizeByte} byte."
        ){}

        public DataTooLongException(string eccLevel, string encodingMode, int version, int maxSizeByte) : base(
            $"The given payload exceeds the maximum size of the QR code standard. The maximum size allowed for the choosen paramters (ECC level={eccLevel}, EncodingMode={encodingMode}, FixedVersion={version}) is {maxSizeByte} byte."
        )
        { }
    }
}
