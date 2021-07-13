//
// Copyright Gallagher Group Ltd 2021
//
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

#if NETSTANDARD2_1_OR_GREATER
using ReadOnlyByteSpan = System.ReadOnlySpan<byte>;
using ByteSpan = System.Span<byte>;
#else
using ReadOnlyByteSpan = Gallagher.LibAsn.Shims.ReadOnlySpan<byte>;
using ByteSpan = Gallagher.LibAsn.Shims.Span<byte>;
#endif

#nullable enable

/// <summary>This knows how to serialize and deserialize ASN.1 data</summary>
namespace Gallagher.LibAsn
{
    public enum AsnUniversalType : byte
    {
        // There is a means to encode arbitrarily large tag numbers using multiple bytes (the "high tag number" form), but this is not typically necessary.
        Integer = 0x02, // an asn.1 integer is arbitrarily long so we need to allow for reading and writing this as binary
        BitString = 0x03,
        OctetString = 0x04,
        Null = 0x05,
        ObjectIdentifier = 0x06,
        Utf8String = 0x0C,
        Sequence = 0x10, // You always see 0x30 rather than 0x10 in an ASN1 binary stream because this always has the 'constructed' bit set
        Set = 0x11, // You always see 0x31 rather than 0x11 in an ASN1 binary stream because this always has the 'constructed' bit set
        PrintableString = 0x13,
        Ia5String = 0x16,
        UtcTime = 0x17,
        GeneralizedTime = 0x18
    }

    public static class AsnUniversalTypeHelper
    {
        // can't override ToString for enums in C#
        public static string StringValue(this AsnUniversalType type) => type switch {
            AsnUniversalType.Integer => "INTEGER",// asn1 uses these ALLCAPS descriptions so we follow suit
            AsnUniversalType.BitString => "BIT STRING",
            AsnUniversalType.OctetString => "OCTET STRING",
            AsnUniversalType.Null => "NULL",
            AsnUniversalType.ObjectIdentifier => "OBJECT IDENTIFIER",
            AsnUniversalType.Utf8String => "UTF8String",
            AsnUniversalType.Sequence => "SEQUENCE",
            AsnUniversalType.Set => "SET",
            AsnUniversalType.PrintableString => "PrintableString",
            AsnUniversalType.Ia5String => "IA5String",
            AsnUniversalType.UtcTime => "UTCTime",
            AsnUniversalType.GeneralizedTime => "GeneralizedTime",
            _ => "??",
        };

        // C# allows any random byte to be cast into an AsnUniversalType, so we perform some validation
        public static bool IsKnownType(byte b)
        {
            switch (b)
            {
                case (byte)AsnUniversalType.Integer:
                case (byte)AsnUniversalType.BitString:
                case (byte)AsnUniversalType.OctetString:
                case (byte)AsnUniversalType.Null:
                case (byte)AsnUniversalType.ObjectIdentifier:
                case (byte)AsnUniversalType.Utf8String:
                case (byte)AsnUniversalType.Sequence:
                case (byte)AsnUniversalType.Set:
                case (byte)AsnUniversalType.PrintableString:
                case (byte)AsnUniversalType.Ia5String:
                case (byte)AsnUniversalType.UtcTime:
                case (byte)AsnUniversalType.GeneralizedTime:
                    return true;
                default:
                    return false;
            }
        }
    }

    /*
    Class              Bit8 Bit7
    Universal          0    0
    Application        0    1
    Context-specific   1    0
    Private            1    1
    */
    // Note we are trying to mimic the following swift enum:
    //enum AsnTag : Equatable {
    //case universal(constructed: Bool, type: AsnUniversalType)
    //case application(constructed: Bool, value:UInt8)
    //case contextSpecific(constructed: Bool, value:UInt8)
    //case `private`(constructed: Bool, value:UInt8)
    //}
    public class AsnTag : IEquatable<AsnTag>
    {
        // abstract class without any abstract methods
        protected AsnTag(bool isConstructed) => IsConstructed = isConstructed;

        public bool IsConstructed { get; }

        public override bool Equals(object? obj) => obj is AsnTag other && Equals(other);

        public virtual bool Equals(AsnTag? other) => other != null && IsConstructed == other.IsConstructed;

        public override int GetHashCode() => IsConstructed.GetHashCode();

        public static bool operator ==(AsnTag? a, AsnTag? b)
        {
            if (!(a is null) && !(b is null)) // can't call == null or that recurses
                return a.Equals(b);

            return a is null && b is null;
        }
        public static bool operator !=(AsnTag? a, AsnTag? b) => !(a == b);

        // Contains cached singleton values for common tags to avoid needless allocation of heap-objects.
        public static class Simple
        {
            public static Universal Null { get; } = new Universal(false, AsnUniversalType.Null);
            public static Universal Integer { get; } = new Universal(false, AsnUniversalType.Integer);
            public static Universal ObjectIdentifier { get; } = new Universal(false, AsnUniversalType.ObjectIdentifier);
            public static Universal Ia5String { get; } = new Universal(false, AsnUniversalType.Ia5String);
            public static Universal PrintableString { get; } = new Universal(false, AsnUniversalType.PrintableString);
            public static Universal Utf8String { get; } = new Universal(false, AsnUniversalType.Utf8String);
            public static Universal BitString { get; } = new Universal(false, AsnUniversalType.BitString);
            public static Universal UtcTime { get; } = new Universal(false, AsnUniversalType.UtcTime);
            public static Universal GeneralizedTime { get; } = new Universal(false, AsnUniversalType.GeneralizedTime);
            public static Universal Sequence { get; } = new Universal(true, AsnUniversalType.Sequence); // sequence is always constructed
        }

        public class Universal : AsnTag
        {
            public Universal(bool isConstructed, AsnUniversalType type) : base(isConstructed) => Type = type;

            public AsnUniversalType Type { get; }

            public override bool Equals(AsnTag? other) => base.Equals(other) && other is Universal u && u.Type == Type;
            public override int GetHashCode() => base.GetHashCode() ^ Type.GetHashCode(); // we don't expect anyone to be hashing this so poor HashCode is acceptable
        }

        public class Application : AsnTag
        {
            public Application(bool isConstructed, byte value) : base(isConstructed) => Value = value;

            public byte Value { get; }

            public override bool Equals(AsnTag? other) => base.Equals(other) && other is Application a && a.Value == Value;
            public override int GetHashCode() => base.GetHashCode() ^ Value.GetHashCode();
        }

        public class ContextSpecific : AsnTag
        {
            public ContextSpecific(bool isConstructed, byte value) : base(isConstructed) => Value = value;

            public byte Value { get; }

            public override bool Equals(AsnTag? other) => base.Equals(other) && other is ContextSpecific a && a.Value == Value;
            public override int GetHashCode() => base.GetHashCode() ^ Value.GetHashCode();
        }

        public class Private : AsnTag
        {
            public Private(bool isConstructed, byte value) : base(isConstructed) => Value = value;

            public byte Value { get; }

            public override bool Equals(AsnTag? other) => base.Equals(other) && other is Private a && a.Value == Value;
            public override int GetHashCode() => base.GetHashCode() ^ Value.GetHashCode();
        }
    }

    static class AsnTagMask
    {
        public const byte Universal = 0x00;
        public const byte Application = 0x40;
        public const byte ContextSpecific = 0x80;
        public const byte Private = 0xC0;

        public const byte Constructed = 0x20;
    }

    public struct AsnObject : IEquatable<AsnObject>
    {
        // convenience helper for creating key value pairs in a sequence
        public static AsnObject KeyValueSequence(string keyOid, AsnObject valueObject)
            => Sequence(ObjectIdentifier(keyOid), valueObject);

        // convenience helper for creating sequences
        public static AsnObject Sequence(params AsnObject[] children) => new AsnObject(AsnTag.Simple.Sequence, children);

        // convenience helper for creating sequences
        public static AsnObject Sequence(IReadOnlyList<AsnObject> children) => new AsnObject(AsnTag.Simple.Sequence, children);

        // convenience helper for creating integers holding raw unmodified binary values
        public static AsnObject Integer(byte[] value) => new AsnObject(AsnTag.Simple.Integer, value);

        // convenience helper for creating integers
        public static AsnObject Integer(int value) => new AsnObject(AsnTag.Simple.Integer, BitConverter.GetBytes(value));

        // convenience helper for creating unsigned integers. Not particularly efficient but it'll do
        public static AsnObject UnsignedInteger(uint value)
        {
            // Note this will always require 4 bytes to encode even a single digit value, which is technically wrong.
            // need to come back and make this work properly and add unit tests
            var bytes = BitConverter.GetBytes(value);
            if ((bytes[0] & 0x80) != 0) // high bit is set on an unsigned integer which means this would be interpreted as a negative int
            {
                var unsignedBytes = new byte[bytes.Length + 1]; // force a leading 0 byte
                bytes.CopyTo(unsignedBytes, 1);
                return new AsnObject(AsnTag.Simple.Integer, unsignedBytes);
            }
            return new AsnObject(AsnTag.Simple.Integer, bytes);
        }

        // convenience helper for creating a UtcTime. Converts the time to UTC if it is not already.
        // Note: If you have control over the format you should prefer GeneralizedTime; it is identical to UtcTime except it uses 4 digit years which is better
        public static AsnObject UtcTime(DateTime value) => new AsnObject(AsnTag.Simple.UtcTime, EncodeUtcTime(value));

        // convenience helper for creating a GeneralizedTime. Converts the time to UTC if it is not already
        public static AsnObject GeneralizedTime(DateTime value) => new AsnObject(AsnTag.Simple.GeneralizedTime, EncodeGeneralizedTime(value));

        // convenience helper for creating oid's
        public static AsnObject ObjectIdentifier(string str) => new AsnObject(AsnTag.Simple.ObjectIdentifier, EncodeObjectIdentifier(str));

        // convenience helper for creating bit strings
        public static AsnObject BitString(byte[] value) => new AsnObject(AsnTag.Simple.BitString, value);

        // convenience helper for creating utf8 strings
        public static AsnObject Utf8String(string value) => new AsnObject(AsnTag.Simple.Utf8String, Encoding.UTF8.GetBytes(value));

        // mimic a swift enum for "contents"
        // the object either holds nothing, a value, or some children
        // We express this as null, byte[] or List<AsnObject>
        private readonly object? m_contents;

        /// Creates a new object with raw value
        /// - Parameters:
        ///   - tag: The tag
        ///   - value: The value. This is a raw sequence of bytes and is not subject to endianness.
        public AsnObject(AsnTag tag, byte[] data)
        {
            if (tag.IsConstructed)
                throw new ArgumentException("Constructed tag must be initalized with children, not raw data", nameof(tag));

            Tag = tag;
            m_contents = data;
        }

        /// Creates a new container object
        /// - Parameters:
        ///   - tag: The tag
        ///   - children: The children
        public AsnObject(AsnTag tag, IReadOnlyList<AsnObject> children)
        {
            if (!tag.IsConstructed)
                throw new ArgumentException("Non-constructed tag must be initalized with data, not children", nameof(tag));

            Tag = tag;
            m_contents = children;
        }

        public AsnObject(AsnTag tag, params AsnObject[] children) : this(tag, (IReadOnlyList<AsnObject>)children)
        { }

        public AsnTag Tag { get; }

        public int Length
        {
            get
            {
                if (m_contents == null)
                {
                    return 0;
                }
                else if (m_contents is byte[] data)
                {
                    return data.Length;
                }
                else if (m_contents is IReadOnlyList<AsnObject> children)
                {
                    int length = 0;
                    foreach (var child in children)
                    {
                        length += child.OuterLength;
                    }
                    return length;
                }
                else
                {
                    throw new InvalidOperationException("Illegal Asn Object contents: " + m_contents);
                }
            }
        }

        public int HeaderLength
        {
            get
            {
                // the length of the header is at least 2, but possibly more depending on the content length
                int length = Length;
                if (length < 0)
                {
                    throw new InvalidOperationException("AsnObject somehow has negative length?");
                    // Note that because we have signed ints here, and aren't doing any
                    // fancy stuff to support unsigned, our java ASN objects top out at 2GB rather than 4GB.
                }
                else if (length < 128)
                { // short length
                    return 2;
                }
                else if (length < 256)
                {
                    return 3; // 1 byte indicator + 1 byte length
                }
                else if (length < 65536)
                {
                    return 4; // 1 byte indicator + 2 byte length
                }
                else if (length < 16777216)
                {
                    return 5; // 1 byte indicator + 3 byte length
                }
                else
                {
                    return 6; // 1 byte indicator + 4 byte length
                }
            }
        }

        /// The entire length of (header + contents)
        public int OuterLength
        {
            get
            {
                if (IsEmpty)
                    return 0;

                return HeaderLength + Length;
            }
        }

        /// If the contents holds a Data value, this returns it.
        public byte[]? Value => m_contents is byte[] data ? data : null;

        /// If the contents holds an array of children, this returns them
        public IReadOnlyList<AsnObject>? Children => m_contents is IReadOnlyList<AsnObject> children ? children : null;

        public override bool Equals(object? obj) => obj is AsnObject o && Equals(o);

        public bool Equals(AsnObject other)
        {
            if (Tag != other.Tag)
            {
                return false;
            }
            if (m_contents == null && other.m_contents == null)
            {
                return true;
            }
            if (m_contents is byte[] myData && other.m_contents is byte[] otherData)
            {
                return myData.SequenceEqual(otherData);
            }
            if (m_contents is IReadOnlyList<AsnObject> myChildren && other.m_contents is IReadOnlyList<AsnObject> otherChildren)
            {
                return myChildren.SequenceEqual(otherChildren);
            }
            return false;
        }

        public static bool operator ==(AsnObject a, AsnObject b) => a.Equals(b);

        public static bool operator !=(AsnObject a, AsnObject b) => !a.Equals(b);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 27;
                hash = (13 * hash) + Tag.GetHashCode();
                if (m_contents is byte[] myData)
                {
                    foreach (byte b in myData)
                        hash = (13 * hash) + b.GetHashCode();
                }
                else if (m_contents is IReadOnlyList<AsnObject> myChildren)
                {
                    foreach (var child in myChildren)
                        hash = (13 * hash) + child.GetHashCode();
                }
                return hash;
            }
        }

        // does not check the tag, just the payload
        public bool IsEmpty => m_contents == null;

        public int? AsInteger()
        {
            if (!Tag.IsConstructed && m_contents is byte[] data && data.Length <= 4)
            {
                if (Tag is AsnTag.Universal ut && ut.Type != AsnUniversalType.Integer)
                    return null;

                // if we get here we are either a Universal(INTEGER) which should be an int, or a custom tag which we assume might be one
                return DecodeInteger(data);
            }
            // this is a constructed object, or some other universal type that's not an integer. Don't attempt to parse
            return null;
        }

        public override string ToString() => ToString(DateTimeFormatInfo.CurrentInfo);

        public string ToString(IFormatProvider formatProvider) => ToString(formatProvider, 4);

        public string ToString(IFormatProvider formatProvider, int indent)
        {
            StringBuilder str = new StringBuilder();
            PrintTo(str, formatProvider, 0, indent);
            return str.ToString();
        }

        private void PrintTo(StringBuilder str, IFormatProvider formatProvider, int indentCurrent, int indentIncreaseBy)
        {
            //noinspection StatementWithEmptyBody
            if (m_contents == null)
            {
                // print nothing
            }
            else if (m_contents is IReadOnlyList<AsnObject> children)
            {
                PrintName(Tag, Length, str, indentCurrent);
                foreach (var child in children)
                {
                    child.PrintTo(str, formatProvider, indentCurrent + indentIncreaseBy, indentIncreaseBy);
                }
            }
            else if (m_contents is byte[] data)
            {
                PrintName(Tag, Length, str, indentCurrent);
                // print the value, indented in
                PrintValue(Tag, data, formatProvider, str, indentCurrent + indentIncreaseBy);
            }
            else
            {
                throw new InvalidOperationException("Illegal Asn Object contents: " + m_contents);
            }
        }

#if NETSTANDARD2_1_OR_GREATER
        public static AsnObject? DerDecode(System.ReadOnlySpan<byte> data) => DerDecodeInternal(data);
#endif
        public static AsnObject? DerDecode(ArraySegment<byte> data) => DerDecodeInternal(new ReadOnlyByteSpan(data.Array, data.Offset, data.Count));

        public static AsnObject? DerDecode(byte[] data) => DerDecodeInternal(new ReadOnlyByteSpan(data));

        public static AsnObject? DerDecode(byte[] data, int rangeOffset, int rangeLength) => DerDecodeInternal(new ReadOnlyByteSpan(data, rangeOffset, rangeLength));

        private static AsnObject? DerDecodeInternal(ReadOnlyByteSpan data)
        {
            if (data.Length < 2)
            { // too short, we must always have at least one byte tag and one byte length
                return null;
            }

            var (rawTag, contentsLen, headerLen) = DecodeHeader(data);

            var tag = DecodeTag(rawTag);
            if (tag == null)
                return null; // can't parse rawTag... should probably throw an exception here to communicate what the problem was

            if (tag.IsConstructed)
            { // constructed tags
                var children = new List<AsnObject>();
                int offset = headerLen;
                while (offset < (headerLen + contentsLen))
                {
                    int remainingStart = offset;
                    int remainingLength = data.Length - offset;

                    if (DerDecodeInternal(data.Slice(remainingStart, remainingLength)) is AsnObject child)
                    {
                        children.Add(child);
                        offset += child.OuterLength;
                    }
                    else
                    {
                        break; // end of stream
                    }
                }
                return new AsnObject(tag, children);

            }
            else
            { // plain old data
                int startIndex = headerLen;
                int endIndex = startIndex + contentsLen;
                if (endIndex > data.Length)
                { // we don't have enough bytes in the buffer for the stated length
                    return null;
                }

                return new AsnObject(tag, data.Slice(headerLen, contentsLen).ToArray());
            }
        }


        public byte[] DerEncode()
        {
            byte[] buffer = new byte[OuterLength];
            int bytesWritten = DerEncodeTo(
                    buffer,
                    0,
                    buffer.Length);
            Debug.Assert(bytesWritten == buffer.Length);
            return buffer;
        }

        // TODO it'd be nice to have this write to a Span or ArraySegment, not just buffer, offset, length
        public int DerEncodeTo(byte[] buffer, int rangeOffset, int rangeLength)
        {
            if (m_contents == null)
            {
                return 0; // don't write empty objects
            }
            if (m_contents is IReadOnlyList<AsnObject> children)
            {
                int bytesWritten = EncodeHeaderTo(buffer, rangeOffset, EncodeTag(Tag), HeaderLength, Length);

                foreach (var child in children)
                {
                    int childRangeOffset = rangeOffset + bytesWritten;
                    int childRangeLength = (rangeLength - childRangeOffset); // range to the end of the buffer
                    bytesWritten += child.DerEncodeTo(buffer, childRangeOffset, childRangeLength);
                }
                return bytesWritten;
            }
            if (m_contents is byte[] value)
            {
                int headerLen = EncodeHeaderTo(buffer, rangeOffset, EncodeTag(Tag), HeaderLength, Length);

                int start = rangeOffset + headerLen;
                Buffer.BlockCopy(value, 0, buffer, start, value.Length);
                return headerLen + value.Length;
            }
            throw new InvalidOperationException("AsnObject has unhandled contents of " + m_contents);
        }

        private static byte EncodeTag(AsnTag tag)
        {
            if (tag is AsnTag.Universal ut)
                return (byte)((byte)ut.Type | AsnTagMask.Universal | (tag.IsConstructed ? AsnTagMask.Constructed : 0));
            else if (tag is AsnTag.ContextSpecific cs)
                return (byte)(cs.Value | AsnTagMask.ContextSpecific | (tag.IsConstructed ? AsnTagMask.Constructed : 0));
            else if (tag is AsnTag.Application ap)
                return (byte)(ap.Value | AsnTagMask.Application | (tag.IsConstructed ? AsnTagMask.Constructed : 0));
            else if (tag is AsnTag.Private pr)
                return (byte)(pr.Value | AsnTagMask.Private | (tag.IsConstructed ? AsnTagMask.Constructed : 0));
            else
                throw new ArgumentException($"Unhandled asn tag type {tag.GetType()}", nameof(tag));
        }

        private static int EncodeHeaderTo(byte[] buffer, int rangeOffset, byte rawTag, int headerLen, int len)
        {
            buffer[rangeOffset] = rawTag;

            switch (headerLen)
            {
                case 2:
                    buffer[rangeOffset + 1] = (byte)(len & 0xff);
                    break;
                case 3:
                case 4:
                case 5:
                case 6:
                    int bytesOfLength = headerLen - 2;
                    buffer[rangeOffset + 1] = (byte)(bytesOfLength | 0x80); // set the high bit
                    for (int i = 0; i < bytesOfLength; i++)
                    { // length is big endian in asn1
                        buffer[rangeOffset + (bytesOfLength - i) + 1] = (byte)((len >> (8 * i)) & 0xff);
                    }
                    break;
                default:
                    throw new ArgumentException($"header length {headerLen} not supported", nameof(headerLen));
            }
            return headerLen;
        }

        private static AsnTag? DecodeTag(byte b)
        {
            var isConstructed = false;
            if ((b & AsnTagMask.Constructed) != 0)
            {
                isConstructed = true;
                b = (byte)(b & (~AsnTagMask.Constructed));
            }

            if ((b & AsnTagMask.Private) == AsnTagMask.Private)
            {
                return new AsnTag.Private(isConstructed, (byte)(b & (~AsnTagMask.Private)));
            }
            else if ((b & AsnTagMask.ContextSpecific) == AsnTagMask.ContextSpecific)
            {
                return new AsnTag.ContextSpecific(isConstructed, (byte)(b & (~AsnTagMask.ContextSpecific)));
            }
            else if ((b & AsnTagMask.Application) == AsnTagMask.Application)
            {
                return new AsnTag.Application(isConstructed, (byte)(b & (~AsnTagMask.Application)));
            }
            else
            {
                return AsnUniversalTypeHelper.IsKnownType(b) ?
                    new AsnTag.Universal(isConstructed, (AsnUniversalType)b) :
                    null;
            }
        }

        struct DecodedHeader
        {
            public readonly byte RawTag;
            public readonly int ContentsLen;
            public readonly int HeaderLen;

            internal DecodedHeader(byte rawTag, int contentsLen, int headerLen)
            {
                RawTag = rawTag;
                ContentsLen = contentsLen;
                HeaderLen = headerLen;
            }

            public void Deconstruct(out byte rawTag, out int contentsLen, out int headerLen)
            {
                rawTag = RawTag;
                contentsLen = ContentsLen;
                headerLen = HeaderLen;
            }
        }

        private static DecodedHeader DecodeHeader(ReadOnlyByteSpan data)
        {
            byte rawTag = data[0];
            byte firstLengthByte = data[1];

            int contentsLen = 0;
            int headerLen;
            if ((firstLengthByte & 0x80) == 0)
            { // short tag
                contentsLen = firstLengthByte;
                headerLen = 2;
            }
            else
            { // long tag
                int bytesOfLength = firstLengthByte & 0x7F; // clear high bit
                if (bytesOfLength > 6)
                {
                    throw new ArgumentException("asn objects larger than 2gb not supported", nameof(data));
                }
                for (int i = 0; i < bytesOfLength; i++)
                { // length is big endian in asn1
                    int dataIdx = (bytesOfLength - i) + 1;
                    contentsLen |= ((data[dataIdx] & 0xff) << (8 * i)); // promote to int before shifting
                }
                headerLen = 2 + bytesOfLength;
            }
            return new DecodedHeader(rawTag, contentsLen, headerLen);
        }

        // Thanks BouncyCastle (under Apache2 license)
        // https://github.com/bcgit/bc-csharp/blob/99467b8431c1a871792ecb34fd5eeb962353b1d2/crypto/src/asn1/DerObjectIdentifier.cs#L112
        private static void WriteField(Stream outputStream, long fieldValue)
        {
            byte[] result = new byte[9];
            int pos = 8;
            result[pos] = (byte)(fieldValue & 0x7f);
            while (fieldValue >= (1L << 7))
            {
                fieldValue >>= 7;
                result[--pos] = (byte)((fieldValue & 0x7f) | 0x80);
            }
            outputStream.Write(result, pos, 9 - pos);
        }

        public static byte[] EncodeObjectIdentifier(string str)
        {
            var outputStream = new MemoryStream();
            int first = 0;
            long firstBuf = 0;
            foreach (var token in str.Split('.'))
            {
                if (first == 0)
                { // first component
                    firstBuf = long.Parse(token) * 40; // just convert garbage into zeroes rather than throwing
                    first = 1;
                }
                else if (first == 1)
                { // second component
                    WriteField(outputStream, firstBuf + long.Parse(token));
                    first = 2;
                }
                else
                { // subsequent components
                    WriteField(outputStream, long.Parse(token));
                }
            }
            return outputStream.ToArray();
        }

        public static string DecodeObjectIdentifier(byte[] data)
        {
            // thanks BouncyCastle for the logic:
            // https://github.com/bcgit/bc-csharp/blob/99467b8431c1a871792ecb34fd5eeb962353b1d2/crypto/src/asn1/DerObjectIdentifier.cs#L269
            // BC-CSharp is licensed under MIT so we can use their code as a reference

            StringBuilder objId = new StringBuilder();
            long value = 0;
            bool first = true;

            for (int i = 0; i != data.Length; i++)
            {
                int b = data[i];

                value += (b & 0x7f);
                if ((b & 0x80) == 0)             // end of number reached
                {
                    if (first)
                    {
                        if (value < 40)
                        {
                            objId.Append('0');
                        }
                        else if (value < 80)
                        {
                            objId.Append('1');
                            value -= 40;
                        }
                        else
                        {
                            objId.Append('2');
                            value -= 80;
                        }
                        first = false;
                    }

                    objId.Append('.');
                    objId.Append(value);
                    value = 0;
                }
                else
                {
                    value <<= 7;
                }
            }
            return objId.ToString();
        }

        public static int DecodeInteger(byte[] data) => DecodeInteger(data, 0, data.Length);

        public static int DecodeInteger(byte[] data, int rangeOffset, int rangeLength)
        {
            if (rangeLength > 4)
                throw new ArgumentOutOfRangeException(nameof(rangeLength), "DecodeInteger can only support 32 bit integers");

            int result = 0;
            for (int i = 0; i < rangeLength; i++)
            {
                result |= (data[rangeOffset + i] << (i * 8));
            }
            return result;
        }

        /** 
         * UTCTime represents a date and time as YYMMDDhhmm[ss], 
         * with an optional timezone offset or “Z” to represent Zulu (aka UTC aka 0 timezone offset). 
         * For instance the UTCTimes 820102120000Z and 820102070000-0500 both represent the same time: 
         * January 2nd, 1982, at 7am in New York City (UTC-5) and at 12pm in UTC.
         * 
         * NOTE at this point we only support RFC 5280 formatted times which is a subset of ASN1 times
         * 
         * Since UTCTime is ambiguous as to whether it’s the 1900’s or 2000’s, RFC 5280 clarifies that it represents dates from 1950 to 2050.
         * RFC 5280 also requires that the “Z” timezone must be used and seconds must be included.
         *
         * NOTE: throws a numberFormatException */
        public static DateTime DecodeUtcTime(byte[] data)
        {
            var asciiRepresentation = Encoding.ASCII.GetString(data);
            int year = int.Parse(asciiRepresentation.Substring(0, 2));
            year += (year < 50) ? 2000 : 1900;

            int month = int.Parse(asciiRepresentation.Substring(2, 2));
            int day = int.Parse(asciiRepresentation.Substring(4, 2));

            int hours = int.Parse(asciiRepresentation.Substring(6, 2));
            int minutes = int.Parse(asciiRepresentation.Substring(8, 2));
            int seconds = int.Parse(asciiRepresentation.Substring(10, 2));

            return new DateTime(year, month, day, hours, minutes, seconds, DateTimeKind.Utc);
        }

        public static byte[] EncodeUtcTime(DateTime dateTime)
            => Encoding.ASCII.GetBytes(
                dateTime.ToUniversalTime().ToString("yyMMddHHmmssZ"));

        /** GeneralizedTime is the same as UTCTime but uses four digits for years instead of two:
         * YYYYMMDDHHMMSSZ
         * 
         * As above we only support RFC 5280 formatted times which is a subset of ASN1 times
         */
        public static DateTime DecodeGeneralizedTime(byte[] data)
        {
            // note we use .Parse rather than TryParse because if we have unparseable data we WANT the exception to be thrown.
            var asciiRepresentation = Encoding.ASCII.GetString(data);
            int year = int.Parse(asciiRepresentation.Substring(0, 4));
            int month = int.Parse(asciiRepresentation.Substring(4, 2));
            int day = int.Parse(asciiRepresentation.Substring(6, 2));

            int hours = int.Parse(asciiRepresentation.Substring(8, 2));
            int minutes = int.Parse(asciiRepresentation.Substring(10, 2));
            int seconds = int.Parse(asciiRepresentation.Substring(12, 2));

            return new DateTime(year, month, day, hours, minutes, seconds, DateTimeKind.Utc);
        }

        public static byte[] EncodeGeneralizedTime(DateTime dateTime)
            => Encoding.ASCII.GetBytes(
                dateTime.ToUniversalTime().ToString("yyyyMMddHHmmssZ"));

        private static string DisplayName(AsnTag tag)
        {
            var rawValue = $"0x{EncodeTag(tag):X02}";
            if (tag is AsnTag.Universal ut)
                return $"{ut.Type.StringValue()} ({rawValue})";
            else if (tag is AsnTag.Application a)
                return $"[APPLICATION {a.Value}] ({rawValue})";
            else if (tag is AsnTag.ContextSpecific c)
                return $"[{c.Value}] ({rawValue})";
            else if (tag is AsnTag.Private p)
                return $"[PRIVATE {p.Value}] ({rawValue})";

            throw new ArgumentException("Unhandled AsnTag class", nameof(tag));
        }

        private static void PrintName(AsnTag tag, int length, StringBuilder toBuffer, int indent)
        {
            toBuffer.AppendFormat("{0}{1} length:{2}{3}",
                new string(' ', indent),
                DisplayName(tag),
                length,
                Environment.NewLine);
        }

        private static void PrintValue(AsnTag tag, byte[] data, IFormatProvider formatProvider, StringBuilder toBuffer, int indent)
        {
            // custom formatting goes here
            if (tag == AsnTag.Simple.Integer && data.Length <= 4)
            {
                toBuffer.AppendFormat("{0}{1}{2}",
                        new string(' ', indent),
                        DecodeInteger(data),
                        Environment.NewLine);
            }
            else if (tag == AsnTag.Simple.Ia5String || tag == AsnTag.Simple.PrintableString)
            {
                toBuffer.AppendFormat("{0}\"{1}\"{2}",
                            new string(' ', indent),
                            Encoding.ASCII.GetString(data),
                            Environment.NewLine);
            }
            else if (tag == AsnTag.Simple.Utf8String)
            {
                toBuffer.AppendFormat("{0}\"{1}\"{2}",
                            new string(' ', indent),
                            Encoding.UTF8.GetString(data),
                            Environment.NewLine);
            }
            else if (tag == AsnTag.Simple.UtcTime)
            {
                toBuffer.AppendFormat("{0}{1} ({2}){3}",
                            new string(' ', indent),
                            DecodeUtcTime(data).ToString(formatProvider), // Note this invokes the default DateTime.ToString which is locale-specific so you might get failed unit tests if you run in the USA
                            Encoding.ASCII.GetString(data),
                            Environment.NewLine);
            }
            else if (tag == AsnTag.Simple.GeneralizedTime)
            {
                toBuffer.AppendFormat("{0}{1} ({2}){3}",
                            new string(' ', indent),
                            DecodeGeneralizedTime(data).ToString(formatProvider), // Note this invokes the default DateTime.ToString which is locale-specific so you might get failed unit tests if you run in the USA
                            Encoding.ASCII.GetString(data),
                            Environment.NewLine);
            }
            else if (tag == AsnTag.Simple.ObjectIdentifier)
            {
                var oid = DecodeObjectIdentifier(data);
                if (AsnObjectIdentifiers.Description(oid) is string description)
                {
                    toBuffer.AppendFormat("{0}{1} ({2}){3}",
                            new string(' ', indent),
                            oid,
                            description,
                            Environment.NewLine);
                }
                else
                {
                    toBuffer.AppendFormat("{0}{1}{2}",
                            new string(' ', indent),
                            oid,
                            Environment.NewLine);
                }
            }

            PrintRawValue(data, toBuffer, indent);
        }

        // prints the byte value in hex
        private static void PrintRawValue(byte[] data, StringBuilder toBuffer, int indent)
        {
            var e = data.GetEnumerator();
            int idx = 0; // 16 bytes per line, hex values with - between them
            while (e.MoveNext())
            {
                if (idx++ % 16 == 0)
                {
                    if (idx > 1)
                        toBuffer.AppendLine(); // close off prev line

                    toBuffer.Append(new string(' ', indent));
                }
                toBuffer.AppendFormat("{0:X2}{1}", e.Current, (idx % 16 == 0 || idx == data.Length ? "" : "-"));
            }
            toBuffer.AppendLine();
        }
    }

    // some known object id's
    // http://www.oid-info.com/cgi-bin/display?tree=2.5.4#focus
    public static class AsnObjectIdentifiers
    {
        public const string AttributeType = "2.5.4";
        public const string CommonName = AttributeType + ".3";
        public const string Surname = AttributeType + ".4";
        public const string SerialNumber = AttributeType + ".5";
        public const string CountryName = AttributeType + ".6";
        public const string LocalityName = AttributeType + ".7";
        public const string StateOrProvinceName = AttributeType + ".8";
        public const string StreetAddress = AttributeType + ".9";
        public const string OrganizationName = AttributeType + ".10";
        public const string OrganizationalUnitName = AttributeType + ".11";

        public const string CertificateExtension = "2.5.29";
        public const string SubjectKeyIdentifier = CertificateExtension + ".14";
        public const string KeyUsage = CertificateExtension + ".15";
        public const string SubjectAltName = CertificateExtension + ".17";
        public const string IssuerAltName = CertificateExtension + ".18";
        public const string BasicConstraints = CertificateExtension + ".19";
        public const string AuthorityKeyIdentifier = CertificateExtension + ".35";

        public const string Rsa = "1.2.840.113549";
        public const string Pkcs = Rsa + ".1";
        public const string Pkcs1 = Pkcs + ".1";
        public const string RsaEncryption = Pkcs1 + ".1";
        public const string Sha256WithRSAEncryption = Pkcs1 + ".11";
        public const string Pkcs9 = Pkcs + ".9";
        public const string EmailAddress = Pkcs9 + ".1";

        public static string? Description(string oidString) => oidString switch {
            // note these description values are based on the OID standards, not the code, hence they start with lowercase
            AttributeType => "attributeType",
            CommonName => "commonName",
            Surname => "surname",
            SerialNumber => "serialNumber",
            CountryName => "countryName",
            LocalityName => "localityName",
            StateOrProvinceName => "stateOrProvinceName",
            StreetAddress => "streetAddress",
            OrganizationName => "organizationName",
            OrganizationalUnitName => "organizationalUnitName",
            CertificateExtension => "certificateExtension",
            SubjectKeyIdentifier => "subjectKeyIdentifier",
            KeyUsage => "keyUsage",
            SubjectAltName => "subjectAltName",
            IssuerAltName => "issuerAltName",
            BasicConstraints => "basicConstraints",
            AuthorityKeyIdentifier => "authorityKeyIdentifier",
            Rsa => "rsa",
            Pkcs => "pkcs",
            Pkcs1 => "pkcs1",
            RsaEncryption => "rsaEncryption",
            Sha256WithRSAEncryption => "sha256WithRSAEncryption",
            Pkcs9 => "pkcs9",
            EmailAddress => "emailAddress",
            _ => null,
        };
    }
}
