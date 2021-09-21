//
// Copyright Gallagher Group Ltd 2021
//
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

#nullable enable

namespace Gallagher.LibAsn
{
    [TestClass]
    public class LibAsnTests
    {
        static readonly CultureInfo nzCulture = new CultureInfo("en-NZ");

        /*
             Point ::= SEQUENCE {
               x INTEGER OPTIONAL,
               y INTEGER OPTIONAL
             }
             */
        [TestMethod]
        public void DecodeSimpleSequenceOfPoints()
        {
            // Point with only an x coordinate of 9 like so (30 means SEQUENCE here):
            var reference = new byte[] { 0x30, 0x06, 0x02, 0x01, 0x09, 0x02, 0x01, 0x0a };
            var asn = AsnObject.DerDecode(reference)!.Value;

            Assert.AreEqual(new AsnTag.Universal(true, AsnUniversalType.Sequence), asn.Tag);
            Assert.AreEqual(6, asn.Length);

            Assert.AreEqual(2, asn.Children!.Count);

            var i = asn.Children![0];
            Assert.AreEqual(AsnTag.Simple.Integer, i.Tag);
            Assert.AreEqual(1, i.Length);
            Assert.AreEqual(9, i.AsInteger());

            var j = asn.Children![1];
            Assert.AreEqual(AsnTag.Simple.Integer, j.Tag);
            Assert.AreEqual(1, j.Length);
            Assert.AreEqual(10, j.AsInteger());
        }

        /*
         Point ::= SEQUENCE {
           x [0] INTEGER OPTIONAL,
           y [1] INTEGER OPTIONAL
         }
         */
        [TestMethod]
        public void DecodeContextSpecificSequenceOfPoints()
        {
            // x-coordinate: 30 03 80 01 09
            {
                var refXonly = new byte[] { 0x30, 0x03, 0x80, 0x01, 0x09 };
                var asn = AsnObject.DerDecode(refXonly)!.Value;
                Assert.AreEqual(AsnTag.Simple.Sequence, asn.Tag);
                Assert.AreEqual(3, asn.Length);

                Assert.AreEqual(1, asn.Children?.Count);

                var i = asn.Children![0];
                Assert.AreEqual(new AsnTag.ContextSpecific(false, 0), i.Tag);
                Assert.AreEqual(1, i.Length);
                Assert.AreEqual(9, i.AsInteger());
            }

            // y-coordinate: 30 03 81 01 09
            {
                var refYonly = new byte[] { 0x30, 0x03, 0x81, 0x01, 0x09 };
                var asn = AsnObject.DerDecode(refYonly)!.Value;
                Assert.AreEqual(AsnTag.Simple.Sequence, asn.Tag);
                Assert.AreEqual(3, asn.Length);

                Assert.AreEqual(1, asn.Children?.Count);

                var i = asn.Children![0];
                Assert.AreEqual(new AsnTag.ContextSpecific(false, 1), i.Tag);
                Assert.AreEqual(1, i.Length);
                Assert.AreEqual(9, i.AsInteger());
            }

            // both: 30 06 80 01 09 81 01 09
            {
                var refBoth = new byte[] { 0x30, 0x06, 0x80, 0x01, 0x09, 0x81, 0x01, 0x09 };
                var asn = AsnObject.DerDecode(refBoth)!.Value;
                Assert.AreEqual(AsnTag.Simple.Sequence, asn.Tag);
                Assert.AreEqual(6, asn.Length);

                Assert.AreEqual(2, asn.Children?.Count);

                var i = asn.Children![0];
                Assert.AreEqual(new AsnTag.ContextSpecific(false, 0), i.Tag);
                Assert.AreEqual(1, i.Length);
                Assert.AreEqual(9, i.AsInteger());

                var i2 = asn.Children![1];
                Assert.AreEqual(new AsnTag.ContextSpecific(false, 1), i2.Tag);
                Assert.AreEqual(1, i2.Length);
                Assert.AreEqual(9, i2.AsInteger());
            }
        }

        [TestMethod]
        public void DecodeContextSpecificConstructedTag()
        {
            // excerpt from a real x509 certificate. This is context-specific tag 0 with a child of an INTEGER(2)
            // A0 03 02 01 02
            var reference = new byte[] { 0xA0, 0x03, 0x02, 0x01, 0x02 };
            var asn = AsnObject.DerDecode(reference)!.Value;
            Assert.AreEqual(new AsnTag.ContextSpecific(true, 0), asn.Tag);
            Assert.AreEqual(3, asn.Length);

            Assert.AreEqual(1, asn.Children?.Count);

            var i = asn.Children![0];
            Assert.AreEqual(AsnTag.Simple.Integer, i.Tag);
            Assert.AreEqual(1, i.Length);
            Assert.AreEqual(2, i.AsInteger());
        }

        [TestMethod]
        public void EncodeContextSpecificConstructedTag()
        {
            // excerpt from a real x509 certificate. This is context-specific tag 0 with a child of an INTEGER(2)
            // A0 03 02 01 02
            var reference = new byte[] { 0xA0, 0x03, 0x02, 0x01, 0x02 };
            var asn = new AsnObject(new AsnTag.ContextSpecific(true, 0),
                AsnObject.Integer(new byte[] { 2 }));

            var result = asn.DerEncode();
            CollectionAssert.AreEqual(reference, result);
        }

        [TestMethod]
        public void EncodeContextSpecificSequenceOfPoints()
        {
            var asn = AsnObject.Sequence(
                    new AsnObject(new AsnTag.ContextSpecific(false, 0), new byte[] { 0x9 }));

            CollectionAssert.AreEqual(new byte[] { 0x30, 0x03, 0x80, 0x01, 0x09 }, asn.DerEncode());

            var asnB = AsnObject.Sequence(
                    new AsnObject(new AsnTag.ContextSpecific(false, 1), new byte[] { 0x9 }));

            CollectionAssert.AreEqual(new byte[] { 0x30, 0x03, 0x81, 0x01, 0x09 }, asnB.DerEncode());

            var asnC = AsnObject.Sequence(
                new AsnObject(new AsnTag.ContextSpecific(false, 0), new byte[] { 0x9 }),
                new AsnObject(new AsnTag.ContextSpecific(false, 1), new byte[] { 0x9 }));

            CollectionAssert.AreEqual(new byte[] { 0x30, 0x06, 0x80, 0x01, 0x09, 0x81, 0x01, 0x09 }, asnC.DerEncode());
        }

        [TestMethod]
        public void EncodeLongTag_oneByteLen()
        {
            var s132 = Encoding.ASCII.GetBytes("This is quite a long string which encodes to 132 ascii bytes which is a bit longer than the 127 bytes maximum length for a short tag");
            var asn = new AsnObject(AsnTag.Simple.Ia5String, s132);

            var reference = new byte[] {
                0x16, // ia5string
                0x81, // high bit set for long-tag, + 1 for single byte of length
                0x84 // 132 in hex
            }.Concat(s132).ToArray(); // payload follows

            var result = asn.DerEncode();
            CollectionAssert.AreEqual(reference, result);
        }

        [TestMethod]
        public void DecodeLongTag_oneByteLen()
        {
            var s132 = Encoding.ASCII.GetBytes("This is quite a long string which encodes to 132 ascii bytes which is a bit longer than the 127 bytes maximum length for a short tag");

            var reference = new byte[] {
                0x16, // ia5string
                0x81, // high bit set for long-tag, + 1 for single byte of length
                0x84 // 132 in hex
            }.Concat(s132).ToArray(); // payload follows

            var result = AsnObject.DerDecode(reference)!.Value;
            Assert.AreEqual(AsnTag.Simple.Ia5String, result.Tag);
            Assert.AreEqual(132, result.Length);
            CollectionAssert.AreEqual(s132, result.Value);
        }

        [TestMethod]
        public void EncodeLongTag_twoByteLen()
        {
            var s30k = Encoding.ASCII.GetBytes(string.Join("", Enumerable.Repeat("cat", 10000)));
            var asn = new AsnObject(AsnTag.Simple.Ia5String, s30k);

            var reference = new byte[] {
                0x16, // ia5string
                0x82, // high bit set for long-tag | 2 for two bytes of length
                0x75, 0x30  // 30,000 in hex, big endian
            }.Concat(s30k).ToArray(); // payload follows

            var result = asn.DerEncode();
            CollectionAssert.AreEqual(reference, result);
        }

        [TestMethod]
        public void DecodeLongTag_twoByteLen()
        {
            var s30k = Encoding.ASCII.GetBytes(string.Join("", Enumerable.Repeat("cat", 10000)));

            var reference = new byte[] {
                0x16, // ia5string
                0x82, // high bit set for long-tag | 2 for two bytes of length
                0x75, 0x30 // 30,000 in hex, big endian
            }.Concat(s30k).ToArray(); // payload follows

            var result = AsnObject.DerDecode(reference)!.Value;
            Assert.AreEqual(AsnTag.Simple.Ia5String, result.Tag);
            Assert.AreEqual(30000, result.Length);
            CollectionAssert.AreEqual(s30k, result.Value);
        }

        [TestMethod]
        public void EncodeLongTag_threeByteLen()
        {
            var s70k = Encoding.ASCII.GetBytes(string.Join("", Enumerable.Repeat("bananas", 10000)));
            var asn = new AsnObject(AsnTag.Simple.Ia5String, s70k);

            var reference = new byte[] {
                0x16, // ia5string
                0x83, // high bit set for long-tag | 3 for three bytes of length
                0x1, 0x11, 0x70 // 70,000 in hex, big endian
            }.Concat(s70k).ToArray(); // payload follows

            var result = asn.DerEncode();
            CollectionAssert.AreEqual(reference, result);
        }

        [TestMethod]
        public void DecodeLongTag_threeByteLen()
        {
            var s70k = Encoding.ASCII.GetBytes(string.Join("", Enumerable.Repeat("bananas", 10000)));

            var reference = new byte[] {
                0x16, // ia5string
                0x83, // high bit set for long-tag | 2 for two bytes of length
                0x1, 0x11, 0x70 // 70,000 in hex, big endian
            }.Concat(s70k).ToArray(); // payload follows

            var result = AsnObject.DerDecode(reference)!.Value;
            Assert.AreEqual(AsnTag.Simple.Ia5String, result.Tag);
            Assert.AreEqual(70000, result.Length);
            CollectionAssert.AreEqual(s70k, result.Value);
        }

        [TestMethod]
        public void EncodeObjectIdentifier()
        {
            // the OID 1.2.840.113549.1.1.11 (representing sha256WithRSAEncryption) is encoded like so:
            // 06 09 2a 86 48 86 f7 0d 01 01 0b
            var reference = new byte[] { 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x0b };

            var d = AsnObject.EncodeObjectIdentifier("1.2.840.113549.1.1.11");
            CollectionAssert.AreEqual(reference, d);
        }

        [TestMethod]
        public void DecodeObjectIdentifier()
        {
            // the OID 1.2.840.113549.1.1.11 (representing sha256WithRSAEncryption) is encoded like so:
            // 06 09 2a 86 48 86 f7 0d 01 01 0b
            // the 06 is the tag, 09 is the length, then 2a... is the encoded OID

            var str = AsnObject.DecodeObjectIdentifier(new byte[] { 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x0b });
            Assert.AreEqual("1.2.840.113549.1.1.11", str);
        }

        [TestMethod]
        public void EncodeUtcTime()
        {
            var d1 = new DateTime(2019, 09, 25, 6, 24, 47, DateTimeKind.Utc);
            var t1 = AsnObject.EncodeUtcTime(d1);

            CollectionAssert.AreEqual(new byte[] { 0x31, 0x39, 0x30, 0x39, 0x32, 0x35, 0x30, 0x36, 0x32, 0x34, 0x34, 0x37, 0x5A }, t1);

            var d2 = new DateTime(2022, 07, 15, 6, 24, 47, DateTimeKind.Utc);
            var t2 = AsnObject.EncodeUtcTime(d2);

            CollectionAssert.AreEqual(new byte[] { 0x32, 0x32, 0x30, 0x37, 0x31, 0x35, 0x30, 0x36, 0x32, 0x34, 0x34, 0x37, 0x5A }, t2);
        }

        [TestMethod]
        public void DecodeUtcTime()
        {
            // this is the ascii string "190925062447Z" which is 25/09/2019 6:24 AM UTC
            var t1 = new byte[] { 0x31, 0x39, 0x30, 0x39, 0x32, 0x35, 0x30, 0x36, 0x32, 0x34, 0x34, 0x37, 0x5A };
            var d1 = AsnObject.DecodeUtcTime(t1);

            Assert.AreEqual(new DateTime(2019, 09, 25, 6, 24, 47, DateTimeKind.Utc), d1);

            // this is the ascii string "220715062447Z" which is 15/07/2022 6:24 AM UTC
            var t2 = new byte[] { 0x32, 0x32, 0x30, 0x37, 0x31, 0x35, 0x30, 0x36, 0x32, 0x34, 0x34, 0x37, 0x5A };
            var d2 = AsnObject.DecodeUtcTime(t2);

            Assert.AreEqual(new DateTime(2022, 07, 15, 6, 24, 47, DateTimeKind.Utc), d2);
        }

        [TestMethod]
        public void EncodeGeneralizedTime()
        {
            var d1 = new DateTime(2019, 09, 25, 6, 24, 47, DateTimeKind.Utc);
            var t1 = AsnObject.EncodeGeneralizedTime(d1);

            CollectionAssert.AreEqual(new byte[] { 0x32, 0x30, 0x31, 0x39, 0x30, 0x39, 0x32, 0x35, 0x30, 0x36, 0x32, 0x34, 0x34, 0x37, 0x5A }, t1);

            var d2 = new DateTime(2022, 07, 15, 6, 24, 47, DateTimeKind.Utc);
            var t2 = AsnObject.EncodeGeneralizedTime(d2);

            CollectionAssert.AreEqual(new byte[] { 0x32, 0x30, 0x32, 0x32, 0x30, 0x37, 0x31, 0x35, 0x30, 0x36, 0x32, 0x34, 0x34, 0x37, 0x5A }, t2);
        }

        [TestMethod]
        public void DecodeGeneralizedTime()
        {
            // this is the ascii string "20190925062447Z" which is 25/09/2019 6:24 AM UTC
            var t1 = new byte[] { 0x32, 0x30, 0x31, 0x39, 0x30, 0x39, 0x32, 0x35, 0x30, 0x36, 0x32, 0x34, 0x34, 0x37, 0x5A };
            var d1 = AsnObject.DecodeGeneralizedTime(t1);

            Assert.AreEqual(new DateTime(2019, 09, 25, 6, 24, 47, DateTimeKind.Utc), d1);

            // this is the ascii string "20220715062447Z" which is 15/07/2022 6:24 AM UTC
            var t2 = new byte[] { 0x32, 0x30, 0x32, 0x32, 0x30, 0x37, 0x31, 0x35, 0x30, 0x36, 0x32, 0x34, 0x34, 0x37, 0x5A };
            var d2 = AsnObject.DecodeGeneralizedTime(t2);

            Assert.AreEqual(new DateTime(2022, 07, 15, 6, 24, 47, DateTimeKind.Utc), d2);
        }

        [TestMethod]
        public void EncodeUnsignedInteger()
        {
            uint noPaddingRequired = 1496159503; // 0x0f, 0x95, 0x2d, 0x59
            var u1 = AsnObject.UnsignedInteger(noPaddingRequired).DerEncode();

            CollectionAssert.AreEqual(new byte[] { 0x02, 0x04, 0x0f, 0x95, 0x2d, 0x59 }, u1);

            uint paddingRequired = 1496159647; // 0x9f, 0x95, 0x2d, 0x59
            var u2 = AsnObject.UnsignedInteger(paddingRequired).DerEncode();

            CollectionAssert.AreEqual(new byte[] { 0x02, 0x05, 0x00, 0x9f, 0x95, 0x2d, 0x59 }, u2);
        }

        [TestMethod]
        public void PrintOutputsCorrectly_BasicType()
        {
            var asn = AsnObject.Sequence(
                    AsnObject.Integer(new byte[] { 0x9 }),
                    new AsnObject(AsnTag.Simple.Ia5String, Encoding.ASCII.GetBytes("foo")),
                    new AsnObject(AsnTag.Simple.Utf8String, Encoding.UTF8.GetBytes("👨‍👩‍👧‍👧")));

            var str = PlatformNewlines(asn.ToString());
            Assert.AreEqual(PlatformNewlines(@"SEQUENCE (0x30) length:35
    INTEGER (0x02) length:1
        9
        09
    IA5String (0x16) length:3
        ""foo""
        66-6F-6F
    UTF8String (0x0C) length:25
        ""👨‍👩‍👧‍👧""
        F0-9F-91-A8-E2-80-8D-F0-9F-91-A9-E2-80-8D-F0-9F
        91-A7-E2-80-8D-F0-9F-91-A7
"), str);
        }

        static readonly byte[] realCert = Convert.FromBase64String("MIIEATCCAumgAwIBAgIJAIK66vVmi3JcMA0GCSqGSIb3DQEBCwUAMIGWMQswCQYDVQQGEwJOWjETMBEGA1UECAwKU29tZS1TdGF0ZTERMA8GA1UEBwwISGFtaWx0b24xEjAQBgNVBAoMCUdhbGxhZ2hlcjEMMAoGA1UECwwDUiZEMREwDwYDVQQDDAhvcmlvbi1DQTEqMCgGCSqGSIb3DQEJARYbb3Jpb24uZWR3YXJkc0BnYWxsYWdoZXIuY29tMB4XDTE5MDkyNTA2MjQ0N1oXDTIyMDcxNTA2MjQ0N1owgZYxCzAJBgNVBAYTAk5aMRMwEQYDVQQIDApTb21lLVN0YXRlMREwDwYDVQQHDAhIYW1pbHRvbjESMBAGA1UECgwJR2FsbGFnaGVyMQwwCgYDVQQLDANSJkQxETAPBgNVBAMMCG9yaW9uLUNBMSowKAYJKoZIhvcNAQkBFhtvcmlvbi5lZHdhcmRzQGdhbGxhZ2hlci5jb20wggEiMA0GCSqGSIb3DQEBAQUAA4IBDwAwggEKAoIBAQCnfvhzrmk4MnpWmEA0VK5jfO1AkQ/QZum9AbF8f2xyGMbyY3PuKnps0sx0L/2ziX1XQK0G+XexAkj4g853gAlOpaj2RV4Nc5tFumaD0X0pdDVdDQ/rcyZk2HMsOeXDru4q5hCs96JGsKo94/QSZnpHnH2gbYZVTi8yGaiiwkaxKIZeKVGmdr4IcKLud0Qd34PZOgbPFYcoRH+MEBEd1AOHIfLg/robMYzbJkEEkV9yE2cU8GjR84V73dogCw2/6XC3W84LwIJo4/hRYtWUrnDC5f/T0eha8PpvtR+fqAu0inp/xry4PNn51zy8AbECgq3XQwDvfw5Y/aZyFhemXx5lAgMBAAGjUDBOMB0GA1UdDgQWBBQJwBGeNPL+iiOeFJBANRWhVmajcDAfBgNVHSMEGDAWgBQJwBGeNPL+iiOeFJBANRWhVmajcDAMBgNVHRMEBTADAQH/MA0GCSqGSIb3DQEBCwUAA4IBAQBY+K4bj0DmItPl/XtPmGIy7uIYrIYy9IW9XtSAlenYmuOk+pCb4cofky1IMnGSbXYRXCgWdNywtnmZia/cc4iboiW7WyXAySIWMZ+8JuPRIy8ZNh8u8GZX4JWozOi+vfsKB/ayWA1FLYng19mQLQKz47FCFL2pYxS+YRlo3z47D5GH2YtkDGANYppO/NSuJrEYfzCqTru5lVKfoebfAlnjLSaIVZcvhnTl0WyjXgHW+6UczVcg1Z8yd0t0lywr01kiF9UwFWTKxggDz7EwZ5Ry7JsobJ3EGYuLMBN5VxlRqW0NskJ7InNNrnNumZpYLJlWbuLhffkVfUp3teArzD8l");

        [TestMethod]
        public void DecodeRealX509Certificate()
        {
            var asn = AsnObject.DerDecode(realCert)!.Value;

            // ROUND TRIP IT! This is the real acid test
            var data = asn.DerEncode();
            CollectionAssert.AreEqual(realCert, data);

            // check the description to make sure our printing function is good

            // Note: The below will fail if the PC's time settings are 24 hour time, with no AM/PM designator,
            //  as the test string below expects 12 hour time, with day/month order (nz culture).
            nzCulture.DateTimeFormat.AMDesignator = "AM";
            nzCulture.DateTimeFormat.PMDesignator = "PM";
            nzCulture.DateTimeFormat.LongTimePattern = "h:mm:ss tt";

            var str = PlatformNewlines(asn.ToString(nzCulture));

            Assert.AreEqual(PlatformNewlines(@"SEQUENCE (0x30) length:1025
    SEQUENCE (0x30) length:745
        [0] (0xA0) length:3
            INTEGER (0x02) length:1
                2
                02
        INTEGER (0x02) length:9
            00-82-BA-EA-F5-66-8B-72-5C
        SEQUENCE (0x30) length:13
            OBJECT IDENTIFIER (0x06) length:9
                1.2.840.113549.1.1.11 (sha256WithRSAEncryption)
                2A-86-48-86-F7-0D-01-01-0B
            NULL (0x05) length:0

        SEQUENCE (0x30) length:150
            SET (0x31) length:11
                SEQUENCE (0x30) length:9
                    OBJECT IDENTIFIER (0x06) length:3
                        2.5.4.6 (countryName)
                        55-04-06
                    PrintableString (0x13) length:2
                        ""NZ""
                        4E-5A
            SET (0x31) length:19
                SEQUENCE (0x30) length:17
                    OBJECT IDENTIFIER (0x06) length:3
                        2.5.4.8 (stateOrProvinceName)
                        55-04-08
                    UTF8String (0x0C) length:10
                        ""Some-State""
                        53-6F-6D-65-2D-53-74-61-74-65
            SET (0x31) length:17
                SEQUENCE (0x30) length:15
                    OBJECT IDENTIFIER (0x06) length:3
                        2.5.4.7 (localityName)
                        55-04-07
                    UTF8String (0x0C) length:8
                        ""Hamilton""
                        48-61-6D-69-6C-74-6F-6E
            SET (0x31) length:18
                SEQUENCE (0x30) length:16
                    OBJECT IDENTIFIER (0x06) length:3
                        2.5.4.10 (organizationName)
                        55-04-0A
                    UTF8String (0x0C) length:9
                        ""Gallagher""
                        47-61-6C-6C-61-67-68-65-72
            SET (0x31) length:12
                SEQUENCE (0x30) length:10
                    OBJECT IDENTIFIER (0x06) length:3
                        2.5.4.11 (organizationalUnitName)
                        55-04-0B
                    UTF8String (0x0C) length:3
                        ""R&D""
                        52-26-44
            SET (0x31) length:17
                SEQUENCE (0x30) length:15
                    OBJECT IDENTIFIER (0x06) length:3
                        2.5.4.3 (commonName)
                        55-04-03
                    UTF8String (0x0C) length:8
                        ""orion-CA""
                        6F-72-69-6F-6E-2D-43-41
            SET (0x31) length:42
                SEQUENCE (0x30) length:40
                    OBJECT IDENTIFIER (0x06) length:9
                        1.2.840.113549.1.9.1 (emailAddress)
                        2A-86-48-86-F7-0D-01-09-01
                    IA5String (0x16) length:27
                        ""orion.edwards@gallagher.com""
                        6F-72-69-6F-6E-2E-65-64-77-61-72-64-73-40-67-61
                        6C-6C-61-67-68-65-72-2E-63-6F-6D
        SEQUENCE (0x30) length:30
            UTCTime (0x17) length:13
                25/09/2019 6:24:47 AM (190925062447Z)
                31-39-30-39-32-35-30-36-32-34-34-37-5A
            UTCTime (0x17) length:13
                15/07/2022 6:24:47 AM (220715062447Z)
                32-32-30-37-31-35-30-36-32-34-34-37-5A
        SEQUENCE (0x30) length:150
            SET (0x31) length:11
                SEQUENCE (0x30) length:9
                    OBJECT IDENTIFIER (0x06) length:3
                        2.5.4.6 (countryName)
                        55-04-06
                    PrintableString (0x13) length:2
                        ""NZ""
                        4E-5A
            SET (0x31) length:19
                SEQUENCE (0x30) length:17
                    OBJECT IDENTIFIER (0x06) length:3
                        2.5.4.8 (stateOrProvinceName)
                        55-04-08
                    UTF8String (0x0C) length:10
                        ""Some-State""
                        53-6F-6D-65-2D-53-74-61-74-65
            SET (0x31) length:17
                SEQUENCE (0x30) length:15
                    OBJECT IDENTIFIER (0x06) length:3
                        2.5.4.7 (localityName)
                        55-04-07
                    UTF8String (0x0C) length:8
                        ""Hamilton""
                        48-61-6D-69-6C-74-6F-6E
            SET (0x31) length:18
                SEQUENCE (0x30) length:16
                    OBJECT IDENTIFIER (0x06) length:3
                        2.5.4.10 (organizationName)
                        55-04-0A
                    UTF8String (0x0C) length:9
                        ""Gallagher""
                        47-61-6C-6C-61-67-68-65-72
            SET (0x31) length:12
                SEQUENCE (0x30) length:10
                    OBJECT IDENTIFIER (0x06) length:3
                        2.5.4.11 (organizationalUnitName)
                        55-04-0B
                    UTF8String (0x0C) length:3
                        ""R&D""
                        52-26-44
            SET (0x31) length:17
                SEQUENCE (0x30) length:15
                    OBJECT IDENTIFIER (0x06) length:3
                        2.5.4.3 (commonName)
                        55-04-03
                    UTF8String (0x0C) length:8
                        ""orion-CA""
                        6F-72-69-6F-6E-2D-43-41
            SET (0x31) length:42
                SEQUENCE (0x30) length:40
                    OBJECT IDENTIFIER (0x06) length:9
                        1.2.840.113549.1.9.1 (emailAddress)
                        2A-86-48-86-F7-0D-01-09-01
                    IA5String (0x16) length:27
                        ""orion.edwards@gallagher.com""
                        6F-72-69-6F-6E-2E-65-64-77-61-72-64-73-40-67-61
                        6C-6C-61-67-68-65-72-2E-63-6F-6D
        SEQUENCE (0x30) length:290
            SEQUENCE (0x30) length:13
                OBJECT IDENTIFIER (0x06) length:9
                    1.2.840.113549.1.1.1 (rsaEncryption)
                    2A-86-48-86-F7-0D-01-01-01
                NULL (0x05) length:0

            BIT STRING (0x03) length:271
                00-30-82-01-0A-02-82-01-01-00-A7-7E-F8-73-AE-69
                38-32-7A-56-98-40-34-54-AE-63-7C-ED-40-91-0F-D0
                66-E9-BD-01-B1-7C-7F-6C-72-18-C6-F2-63-73-EE-2A
                7A-6C-D2-CC-74-2F-FD-B3-89-7D-57-40-AD-06-F9-77
                B1-02-48-F8-83-CE-77-80-09-4E-A5-A8-F6-45-5E-0D
                73-9B-45-BA-66-83-D1-7D-29-74-35-5D-0D-0F-EB-73
                26-64-D8-73-2C-39-E5-C3-AE-EE-2A-E6-10-AC-F7-A2
                46-B0-AA-3D-E3-F4-12-66-7A-47-9C-7D-A0-6D-86-55
                4E-2F-32-19-A8-A2-C2-46-B1-28-86-5E-29-51-A6-76
                BE-08-70-A2-EE-77-44-1D-DF-83-D9-3A-06-CF-15-87
                28-44-7F-8C-10-11-1D-D4-03-87-21-F2-E0-FE-BA-1B
                31-8C-DB-26-41-04-91-5F-72-13-67-14-F0-68-D1-F3
                85-7B-DD-DA-20-0B-0D-BF-E9-70-B7-5B-CE-0B-C0-82
                68-E3-F8-51-62-D5-94-AE-70-C2-E5-FF-D3-D1-E8-5A
                F0-FA-6F-B5-1F-9F-A8-0B-B4-8A-7A-7F-C6-BC-B8-3C
                D9-F9-D7-3C-BC-01-B1-02-82-AD-D7-43-00-EF-7F-0E
                58-FD-A6-72-16-17-A6-5F-1E-65-02-03-01-00-01
        [3] (0xA3) length:80
            SEQUENCE (0x30) length:78
                SEQUENCE (0x30) length:29
                    OBJECT IDENTIFIER (0x06) length:3
                        2.5.29.14 (subjectKeyIdentifier)
                        55-1D-0E
                    OCTET STRING (0x04) length:22
                        04-14-09-C0-11-9E-34-F2-FE-8A-23-9E-14-90-40-35
                        15-A1-56-66-A3-70
                SEQUENCE (0x30) length:31
                    OBJECT IDENTIFIER (0x06) length:3
                        2.5.29.35 (authorityKeyIdentifier)
                        55-1D-23
                    OCTET STRING (0x04) length:24
                        30-16-80-14-09-C0-11-9E-34-F2-FE-8A-23-9E-14-90
                        40-35-15-A1-56-66-A3-70
                SEQUENCE (0x30) length:12
                    OBJECT IDENTIFIER (0x06) length:3
                        2.5.29.19 (basicConstraints)
                        55-1D-13
                    OCTET STRING (0x04) length:5
                        30-03-01-01-FF
    SEQUENCE (0x30) length:13
        OBJECT IDENTIFIER (0x06) length:9
            1.2.840.113549.1.1.11 (sha256WithRSAEncryption)
            2A-86-48-86-F7-0D-01-01-0B
        NULL (0x05) length:0

    BIT STRING (0x03) length:257
        00-58-F8-AE-1B-8F-40-E6-22-D3-E5-FD-7B-4F-98-62
        32-EE-E2-18-AC-86-32-F4-85-BD-5E-D4-80-95-E9-D8
        9A-E3-A4-FA-90-9B-E1-CA-1F-93-2D-48-32-71-92-6D
        76-11-5C-28-16-74-DC-B0-B6-79-99-89-AF-DC-73-88
        9B-A2-25-BB-5B-25-C0-C9-22-16-31-9F-BC-26-E3-D1
        23-2F-19-36-1F-2E-F0-66-57-E0-95-A8-CC-E8-BE-BD
        FB-0A-07-F6-B2-58-0D-45-2D-89-E0-D7-D9-90-2D-02
        B3-E3-B1-42-14-BD-A9-63-14-BE-61-19-68-DF-3E-3B
        0F-91-87-D9-8B-64-0C-60-0D-62-9A-4E-FC-D4-AE-26
        B1-18-7F-30-AA-4E-BB-B9-95-52-9F-A1-E6-DF-02-59
        E3-2D-26-88-55-97-2F-86-74-E5-D1-6C-A3-5E-01-D6
        FB-A5-1C-CD-57-20-D5-9F-32-77-4B-74-97-2C-2B-D3
        59-22-17-D5-30-15-64-CA-C6-08-03-CF-B1-30-67-94
        72-EC-9B-28-6C-9D-C4-19-8B-8B-30-13-79-57-19-51
        A9-6D-0D-B2-42-7B-22-73-4D-AE-73-6E-99-9A-58-2C
        99-56-6E-E2-E1-7D-F9-15-7D-4A-77-B5-E0-2B-CC-3F
        25
"), str);
        }

        private static string PlatformNewlines(string str) => str
            .Replace("\r\n", "\n") // convert windows endings to unix
            .Replace("\r", "\n") // mac endings to unix
            .Replace("\n", Environment.NewLine); // everything is now unix, and converts to the platform value
    }
}
