// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  ZipWriter.cs (v5.1.0 «FUNDAMENTO») : motor ZIP en memoria para empaquetar
//  OPC (PPTX) — MISMO formato PKWARE APPNOTE.TXT que ZipBackup, pero por
//  pares (nombre → bytes) en vez de por archivos.
//
//  MOTIVO de existir: System.IO.Packaging (WindowsBase.dll) resuelve distinto
//  entre CLR2 (WindowsBase 3.0, presente con .NET 3.5) y CLR4 (WindowsBase
//  4.0) — una app net35 con esa referencia NO puede correr bajo CLR4 sin el
//  ensamblado 3.x del GAC. Al escribir el paquete OPC nosotros mismos, la
//  variante net35 de Lumina.Core queda con CERO dependencias del GAC y corre
//  idéntica bajo .NET 3.5 y .NET 4.8 (requisito: «mínimo 3.5, usa 4.8 si
//  está disponible»).
//
//  Estructura: local file header + data + central directory + EOCD. Deflate
//  por DeflateStream (System.dll 2.0+); CRC32 tabla IEEE 802.3.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace lumina.core
{
    /// <summary>Empaqueta pares (nombre → bytes) en un Stream ZIP válido.</summary>
    public static class ZipWriter
    {
        /// <summary>
        /// Escribe TODAS las entradas en output. La primera entrada del
        /// enumerable se escribe primero (convención: [Content_Types].xml).
        /// Los nombres NO deben traer '/' inicial ni '\' (se normalizan a '/').
        /// Lanza excepción si falla; no libera output (es del llamador).
        /// </summary>
        public static void WriteEntries(Stream output, IEnumerable<KeyValuePair<string, byte[]>> entries)
        {
            if (output == null) throw new ArgumentNullException("output");
            if (entries == null) throw new ArgumentNullException("entries");
            List<Central> central = new List<Central>();
            foreach (KeyValuePair<string, byte[]> kv in entries)
            {
                string name = (kv.Key ?? string.Empty).TrimStart('/').Replace('\\', '/');
                byte[] data = kv.Value ?? new byte[0];
                central.Add(WriteLocal(output, name, data));
            }
            long cdStart = output.Position;
            foreach (Central c in central)
            {
                byte[] nameBytes = Encoding.UTF8.GetBytes(c.Name);
                ZipLow.WriteU32(output, 0x02014b50);          // central signature
                ZipLow.WriteU16(output, 20);                  // version made by
                ZipLow.WriteU16(output, 20);                  // version needed
                ZipLow.WriteU16(output, 0x0800);              // flags: UTF-8
                ZipLow.WriteU16(output, c.Method);
                ZipLow.WriteU16(output, c.TimeDos);
                ZipLow.WriteU16(output, c.DateDos);
                ZipLow.WriteU32(output, c.Crc);
                ZipLow.WriteU32(output, c.CompressedSize);
                ZipLow.WriteU32(output, c.UncompressedSize);
                ZipLow.WriteU16(output, (ushort)nameBytes.Length);
                ZipLow.WriteU16(output, 0);                   // extra
                ZipLow.WriteU16(output, 0);                   // comment
                ZipLow.WriteU16(output, 0);                   // disk start
                ZipLow.WriteU16(output, 0);                   // internal attrs
                ZipLow.WriteU32(output, 0);                   // external attrs
                ZipLow.WriteU32(output, (uint)c.HeaderOffset);
                output.Write(nameBytes, 0, nameBytes.Length);
            }
            long cdSize = output.Position - cdStart;
            ZipLow.WriteU32(output, 0x06054b50);              // EOCD
            ZipLow.WriteU16(output, 0); ZipLow.WriteU16(output, 0);
            ZipLow.WriteU16(output, (ushort)central.Count);
            ZipLow.WriteU16(output, (ushort)central.Count);
            ZipLow.WriteU32(output, (uint)cdSize);
            ZipLow.WriteU32(output, (uint)cdStart);
            ZipLow.WriteU16(output, 0);
        }

        private sealed class Central
        {
            public string Name;
            public uint Crc;
            public uint CompressedSize;
            public uint UncompressedSize;
            public int HeaderOffset;
            public ushort TimeDos;
            public ushort DateDos;
            public ushort Method;
        }

        private static Central WriteLocal(Stream output, string name, byte[] plain)
        {
            Central c = new Central();
            c.Name = name;
            c.HeaderOffset = (int)output.Position;
            DateTime mt = DateTime.Now;
            c.TimeDos = (ushort)((mt.Hour << 11) | (mt.Minute << 5) | (mt.Second >> 1));
            c.DateDos = (ushort)(((mt.Year - 1980) << 9) | (mt.Month << 5) | mt.Day);
            c.UncompressedSize = (uint)plain.Length;
            c.Crc = ZipLow.Crc32(plain, 0, plain.Length);

            byte[] body;
            if (plain.Length == 0)
            {
                body = plain;                                  // stored (method 0)
            }
            else
            {
                MemoryStream packed = new MemoryStream();
                using (DeflateStream ds = new DeflateStream(packed, CompressionMode.Compress, true))
                {
                    ds.Write(plain, 0, plain.Length);
                }
                body = packed.ToArray();
            }
            c.CompressedSize = (uint)body.Length;
            c.Method = (ushort)(plain.Length == 0 ? 0 : 8);

            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            ZipLow.WriteU32(output, 0x04034b50);              // local file header
            ZipLow.WriteU16(output, 20);                      // version needed
            ZipLow.WriteU16(output, 0x0800);                  // flags: UTF-8
            ZipLow.WriteU16(output, c.Method);
            ZipLow.WriteU16(output, c.TimeDos);
            ZipLow.WriteU16(output, c.DateDos);
            ZipLow.WriteU32(output, c.Crc);
            ZipLow.WriteU32(output, c.CompressedSize);
            ZipLow.WriteU32(output, c.UncompressedSize);
            ZipLow.WriteU16(output, (ushort)nameBytes.Length);
            ZipLow.WriteU16(output, 0);                       // extra
            output.Write(nameBytes, 0, nameBytes.Length);
            if (body.Length > 0) output.Write(body, 0, body.Length);
            return c;
        }
    }

    /// <summary>Primitivas ZIP compartidas (ZipWriter + ZipBackup).</summary>
    internal static class ZipLow
    {
        internal static void WriteU16(Stream s, ushort v)
        {
            s.WriteByte((byte)(v & 0xFF));
            s.WriteByte((byte)((v >> 8) & 0xFF));
        }

        internal static void WriteU32(Stream s, uint v)
        {
            s.WriteByte((byte)(v & 0xFF));
            s.WriteByte((byte)((v >> 8) & 0xFF));
            s.WriteByte((byte)((v >> 16) & 0xFF));
            s.WriteByte((byte)((v >> 24) & 0xFF));
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            uint[] t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = ((c & 1) != 0) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                t[i] = c;
            }
            return t;
        }

        internal static uint Crc32(byte[] data, int off, int len)
        {
            uint c = 0xFFFFFFFFu;
            for (int i = off; i < off + len; i++)
                c = CrcTable[(c ^ data[i]) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }
    }
}
