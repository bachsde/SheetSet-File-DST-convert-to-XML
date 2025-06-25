using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;


namespace SheetSet_File_DST_Converter.Helper
{
    public class DstXmlConverter
    {
        private Dictionary<int, int> map;

        public void LoadDictionary(string path, string direction)
        {
            string json = File.ReadAllText(path);
            var root = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(json);
            map = root[direction].ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value);
        }

        public void RestoreDictionary(string xmlPath, string dstPath, string outputDictPath)
        {
            byte[] xmlBytes = File.ReadAllBytes(xmlPath);
            byte[] dstBytes = File.ReadAllBytes(dstPath);

            if (xmlBytes.Length != dstBytes.Length)
                throw new Exception("File lengths do not match");

            var xmlToDst = new Dictionary<int, int>();
            var dstToXml = new Dictionary<int, int>();

            int i = 0;
            while (i < xmlBytes.Length)
            {
                int span = GetUtf8Span(xmlBytes[i]);
                if (i + span > xmlBytes.Length || i + span > dstBytes.Length)
                    throw new Exception("Invalid span at position " + i);

                int xmlCode = JoinBytes(xmlBytes, i, span);
                int dstCode = JoinBytes(dstBytes, i, span);

                if (!xmlToDst.ContainsKey(xmlCode)) xmlToDst[xmlCode] = dstCode;
                if (!dstToXml.ContainsKey(dstCode)) dstToXml[dstCode] = xmlCode;

                i += span;
            }

            var dict = new Dictionary<string, Dictionary<string, int>>
            {
                ["xml_to_dst"] = xmlToDst.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                ["dst_to_xml"] = dstToXml.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(outputDictPath, System.Text.Json.JsonSerializer.Serialize(dict, options));
        }

        public byte[] Convert(byte[] input)
        {
            var output = new List<byte>();
            int i = 0;

            while (i < input.Length)
            {
                bool found = false;

                for (int span = 1; span <= 4; span++)
                {
                    if (i + span > input.Length)
                        break;

                    int codepoint = JoinBytes(input, i, span);

                    if (map.TryGetValue(codepoint, out int mappedCode))
                    {
                        output.AddRange(SplitBytes(mappedCode, span));
                        i += span;
                        found = true;
                        break;
                    }
                }

                if (!found)
                    throw new Exception($"Unmapped byte sequence at position {i}");
            }

            return output.ToArray();
        }

        private int GetUtf8Span(byte b)
        {
            if ((b & 0b10000000) == 0) return 1;
            if ((b & 0b11100000) == 0b11000000) return 2;
            if ((b & 0b11110000) == 0b11100000) return 3;
            if ((b & 0b11111000) == 0b11110000) return 4;
            throw new Exception("Invalid UTF-8 byte: " + b);
        }

        private int JoinBytes(byte[] arr, int start, int count)
        {
            int result = 0;
            for (int i = 0; i < count; i++)
                result = (result << 8) | arr[start + i];
            return result;
        }

        private IEnumerable<byte> SplitBytes(int value, int count)
        {
            byte[] result = new byte[count];
            for (int i = count - 1; i >= 0; i--)
            {
                result[i] = (byte)(value & 0xFF);
                value >>= 8;
            }
            return result;
        }

        public void LoadBuiltIn(string direction)
        {
            var dict = BuiltInDictionary.Data[direction];
            map = dict.ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value);
        }



    }
}
