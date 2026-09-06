using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace WinUIMusicPlayer.Services.Plugins
{
    /// <summary>
    /// QQ 音乐 QRC 加密歌词解密(移植自 BakaMusic lyric-decrypt.ts, 源自 MusicFree customDES):
    /// hex 密文 → 自定义 Triple-DES(KEY1 解密→KEY2 加密→KEY3 解密) → zlib 解压 → UTF-8 文本
    /// → QRC XML 转标准 LRC。
    /// 部分 MusicFree 插件 getLyric 返回的 rawLrc/translation 为加密 hex, 需应用层解密。
    /// </summary>
    public static class QrcLyricDecryptor
    {
        // ============ QRC 自定义 DES S-Box(与标准 DES 不同, 勿改) ============
        private static readonly byte[] SBox1 =
        {
            14, 4, 13, 1, 2, 15, 11, 8, 3, 10, 6, 12, 5, 9, 0, 7,
            0, 15, 7, 4, 14, 2, 13, 1, 10, 6, 12, 11, 9, 5, 3, 8,
            4, 1, 14, 8, 13, 6, 2, 11, 15, 12, 9, 7, 3, 10, 5, 0,
            15, 12, 8, 2, 4, 9, 1, 7, 5, 11, 3, 14, 10, 0, 6, 13,
        };
        private static readonly byte[] SBox2 =
        {
            15, 1, 8, 14, 6, 11, 3, 4, 9, 7, 2, 13, 12, 0, 5, 10,
            3, 13, 4, 7, 15, 2, 8, 15, 12, 0, 1, 10, 6, 9, 11, 5,
            0, 14, 7, 11, 10, 4, 13, 1, 5, 8, 12, 6, 9, 3, 2, 15,
            13, 8, 10, 1, 3, 15, 4, 2, 11, 6, 7, 12, 0, 5, 14, 9,
        };
        private static readonly byte[] SBox3 =
        {
            10, 0, 9, 14, 6, 3, 15, 5, 1, 13, 12, 7, 11, 4, 2, 8,
            13, 7, 0, 9, 3, 4, 6, 10, 2, 8, 5, 14, 12, 11, 15, 1,
            13, 6, 4, 9, 8, 15, 3, 0, 11, 1, 2, 12, 5, 10, 14, 7,
            1, 10, 13, 0, 6, 9, 8, 7, 4, 15, 14, 3, 11, 5, 2, 12,
        };
        private static readonly byte[] SBox4 =
        {
            7, 13, 14, 3, 0, 6, 9, 10, 1, 2, 8, 5, 11, 12, 4, 15,
            13, 8, 11, 5, 6, 15, 0, 3, 4, 7, 2, 12, 1, 10, 14, 9,
            10, 6, 9, 0, 12, 11, 7, 13, 15, 1, 3, 14, 5, 2, 8, 4,
            3, 15, 0, 6, 10, 10, 13, 8, 9, 4, 5, 11, 12, 7, 2, 14,
        };
        private static readonly byte[] SBox5 =
        {
            2, 12, 4, 1, 7, 10, 11, 6, 8, 5, 3, 15, 13, 0, 14, 9,
            14, 11, 2, 12, 4, 7, 13, 1, 5, 0, 15, 10, 3, 9, 8, 6,
            4, 2, 1, 11, 10, 13, 7, 8, 15, 9, 12, 5, 6, 3, 0, 14,
            11, 8, 12, 7, 1, 14, 2, 13, 6, 15, 0, 9, 10, 4, 5, 3,
        };
        private static readonly byte[] SBox6 =
        {
            12, 1, 10, 15, 9, 2, 6, 8, 0, 13, 3, 4, 14, 7, 5, 11,
            10, 15, 4, 2, 7, 12, 9, 5, 6, 1, 13, 14, 0, 11, 3, 8,
            9, 14, 15, 5, 2, 8, 12, 3, 7, 0, 4, 10, 1, 13, 11, 6,
            4, 3, 2, 12, 9, 5, 15, 10, 11, 14, 1, 7, 6, 0, 8, 13,
        };
        private static readonly byte[] SBox7 =
        {
            4, 11, 2, 14, 15, 0, 8, 13, 3, 12, 9, 7, 5, 10, 6, 1,
            13, 0, 11, 7, 4, 9, 1, 10, 14, 3, 5, 12, 2, 15, 8, 6,
            1, 4, 11, 13, 12, 3, 7, 14, 10, 15, 6, 8, 0, 5, 9, 2,
            6, 11, 13, 8, 1, 4, 10, 7, 9, 5, 0, 15, 14, 2, 3, 12,
        };
        private static readonly byte[] SBox8 =
        {
            13, 2, 8, 4, 6, 15, 11, 1, 10, 9, 3, 14, 5, 0, 12, 7,
            1, 15, 13, 8, 10, 3, 7, 4, 12, 5, 6, 11, 0, 14, 9, 2,
            7, 11, 4, 1, 9, 12, 14, 2, 0, 6, 10, 13, 15, 3, 5, 8,
            2, 1, 14, 7, 4, 10, 8, 13, 15, 12, 9, 0, 3, 5, 6, 11,
        };

        private enum DesMode { Encrypt = 0, Decrypt = 1 }

        // 三个固定密钥(来自 QQ 音乐客户端)
        private static readonly byte[] Key1 = Encoding.ASCII.GetBytes("!@#)(NHLiuy*$%^&");
        private static readonly byte[] Key2 = Encoding.ASCII.GetBytes("123ZXC!@#)(*$%^&");
        private static readonly byte[] Key3 = Encoding.ASCII.GetBytes("!@#)(*$%^&abcDEF");

        private static readonly int[] KeyRndShift = { 1, 1, 2, 2, 2, 2, 2, 2, 1, 2, 2, 2, 2, 2, 2, 1 };
        private static readonly int[] KeyPermC = { 56, 48, 40, 32, 24, 16, 8, 0, 57, 49, 41, 33, 25, 17, 9, 1, 58, 50, 42, 34, 26, 18, 10, 2, 59, 51, 43, 35 };
        private static readonly int[] KeyPermD = { 62, 54, 46, 38, 30, 22, 14, 6, 61, 53, 45, 37, 29, 21, 13, 5, 60, 52, 44, 36, 28, 20, 12, 4, 27, 19, 11, 3 };
        private static readonly int[] KeyCompression = { 13, 16, 10, 23, 0, 4, 2, 27, 14, 5, 20, 9, 22, 18, 11, 3, 25, 7, 15, 6, 26, 19, 12, 1, 40, 51, 30, 36, 46, 54, 29, 39, 50, 44, 32, 47, 43, 48, 38, 55, 33, 52, 45, 41, 49, 35, 28, 31 };

        // ============ 位运算辅助(与 JS 版一一对应) ============

        private static uint BitNum(byte[] a, int b, int c)
        {
            int byteIndex = b / 32 * 4 + 3 - (b % 32) / 8;
            int bitPosition = 7 - b % 8;
            int extractedBit = (a[byteIndex] >> bitPosition) & 0x01;
            return (uint)extractedBit << c;
        }

        private static uint BitNumIntR(uint a, int b, int c) => ((a >> (31 - b)) & 0x00000001u) << c;

        private static uint BitNumIntL(uint a, int b, int c) => ((a << b) & 0x80000000u) >> c;

        private static int SBoxBit(int a) => (a & 0x20) | ((a & 0x1f) >> 1) | ((a & 0x01) << 4);

        private static void IpPermutation(uint[] state, byte[] inBytes)
        {
            state[0] =
                BitNum(inBytes, 57, 31) | BitNum(inBytes, 49, 30) | BitNum(inBytes, 41, 29) |
                BitNum(inBytes, 33, 28) | BitNum(inBytes, 25, 27) | BitNum(inBytes, 17, 26) |
                BitNum(inBytes, 9, 25) | BitNum(inBytes, 1, 24) | BitNum(inBytes, 59, 23) |
                BitNum(inBytes, 51, 22) | BitNum(inBytes, 43, 21) | BitNum(inBytes, 35, 20) |
                BitNum(inBytes, 27, 19) | BitNum(inBytes, 19, 18) | BitNum(inBytes, 11, 17) |
                BitNum(inBytes, 3, 16) | BitNum(inBytes, 61, 15) | BitNum(inBytes, 53, 14) |
                BitNum(inBytes, 45, 13) | BitNum(inBytes, 37, 12) | BitNum(inBytes, 29, 11) |
                BitNum(inBytes, 21, 10) | BitNum(inBytes, 13, 9) | BitNum(inBytes, 5, 8) |
                BitNum(inBytes, 63, 7) | BitNum(inBytes, 55, 6) | BitNum(inBytes, 47, 5) |
                BitNum(inBytes, 39, 4) | BitNum(inBytes, 31, 3) | BitNum(inBytes, 23, 2) |
                BitNum(inBytes, 15, 1) | BitNum(inBytes, 7, 0);
            state[1] =
                BitNum(inBytes, 56, 31) | BitNum(inBytes, 48, 30) | BitNum(inBytes, 40, 29) |
                BitNum(inBytes, 32, 28) | BitNum(inBytes, 24, 27) | BitNum(inBytes, 16, 26) |
                BitNum(inBytes, 8, 25) | BitNum(inBytes, 0, 24) | BitNum(inBytes, 58, 23) |
                BitNum(inBytes, 50, 22) | BitNum(inBytes, 42, 21) | BitNum(inBytes, 34, 20) |
                BitNum(inBytes, 26, 19) | BitNum(inBytes, 18, 18) | BitNum(inBytes, 10, 17) |
                BitNum(inBytes, 2, 16) | BitNum(inBytes, 60, 15) | BitNum(inBytes, 52, 14) |
                BitNum(inBytes, 44, 13) | BitNum(inBytes, 36, 12) | BitNum(inBytes, 28, 11) |
                BitNum(inBytes, 20, 10) | BitNum(inBytes, 12, 9) | BitNum(inBytes, 4, 8) |
                BitNum(inBytes, 62, 7) | BitNum(inBytes, 54, 6) | BitNum(inBytes, 46, 5) |
                BitNum(inBytes, 38, 4) | BitNum(inBytes, 30, 3) | BitNum(inBytes, 22, 2) |
                BitNum(inBytes, 14, 1) | BitNum(inBytes, 6, 0);
        }

        private static void InvIp(uint[] state, byte[] inBytes)
        {
            inBytes[3] = (byte)(BitNumIntR(state[1], 7, 7) | BitNumIntR(state[0], 7, 6) |
                BitNumIntR(state[1], 15, 5) | BitNumIntR(state[0], 15, 4) |
                BitNumIntR(state[1], 23, 3) | BitNumIntR(state[0], 23, 2) |
                BitNumIntR(state[1], 31, 1) | BitNumIntR(state[0], 31, 0));
            inBytes[2] = (byte)(BitNumIntR(state[1], 6, 7) | BitNumIntR(state[0], 6, 6) |
                BitNumIntR(state[1], 14, 5) | BitNumIntR(state[0], 14, 4) |
                BitNumIntR(state[1], 22, 3) | BitNumIntR(state[0], 22, 2) |
                BitNumIntR(state[1], 30, 1) | BitNumIntR(state[0], 30, 0));
            inBytes[1] = (byte)(BitNumIntR(state[1], 5, 7) | BitNumIntR(state[0], 5, 6) |
                BitNumIntR(state[1], 13, 5) | BitNumIntR(state[0], 13, 4) |
                BitNumIntR(state[1], 21, 3) | BitNumIntR(state[0], 21, 2) |
                BitNumIntR(state[1], 29, 1) | BitNumIntR(state[0], 29, 0));
            inBytes[0] = (byte)(BitNumIntR(state[1], 4, 7) | BitNumIntR(state[0], 4, 6) |
                BitNumIntR(state[1], 12, 5) | BitNumIntR(state[0], 12, 4) |
                BitNumIntR(state[1], 20, 3) | BitNumIntR(state[0], 20, 2) |
                BitNumIntR(state[1], 28, 1) | BitNumIntR(state[0], 28, 0));
            inBytes[7] = (byte)(BitNumIntR(state[1], 3, 7) | BitNumIntR(state[0], 3, 6) |
                BitNumIntR(state[1], 11, 5) | BitNumIntR(state[0], 11, 4) |
                BitNumIntR(state[1], 19, 3) | BitNumIntR(state[0], 19, 2) |
                BitNumIntR(state[1], 27, 1) | BitNumIntR(state[0], 27, 0));
            inBytes[6] = (byte)(BitNumIntR(state[1], 2, 7) | BitNumIntR(state[0], 2, 6) |
                BitNumIntR(state[1], 10, 5) | BitNumIntR(state[0], 10, 4) |
                BitNumIntR(state[1], 18, 3) | BitNumIntR(state[0], 18, 2) |
                BitNumIntR(state[1], 26, 1) | BitNumIntR(state[0], 26, 0));
            inBytes[5] = (byte)(BitNumIntR(state[1], 1, 7) | BitNumIntR(state[0], 1, 6) |
                BitNumIntR(state[1], 9, 5) | BitNumIntR(state[0], 9, 4) |
                BitNumIntR(state[1], 17, 3) | BitNumIntR(state[0], 17, 2) |
                BitNumIntR(state[1], 25, 1) | BitNumIntR(state[0], 25, 0));
            inBytes[4] = (byte)(BitNumIntR(state[1], 0, 7) | BitNumIntR(state[0], 0, 6) |
                BitNumIntR(state[1], 8, 5) | BitNumIntR(state[0], 8, 4) |
                BitNumIntR(state[1], 16, 3) | BitNumIntR(state[0], 16, 2) |
                BitNumIntR(state[1], 24, 1) | BitNumIntR(state[0], 24, 0));
        }

        private static uint FeistelF(uint state, byte[] key)
        {
            var lrgstate = new byte[6];

            uint t1 =
                BitNumIntL(state, 31, 0) | ((state & 0xf0000000u) >> 1) | BitNumIntL(state, 4, 5) |
                BitNumIntL(state, 3, 6) | ((state & 0x0f000000u) >> 3) | BitNumIntL(state, 8, 11) |
                BitNumIntL(state, 7, 12) | ((state & 0x00f00000u) >> 5) | BitNumIntL(state, 12, 17) |
                BitNumIntL(state, 11, 18) | ((state & 0x000f0000u) >> 7) | BitNumIntL(state, 16, 23);
            uint t2 =
                BitNumIntL(state, 15, 0) | ((state & 0x0000f000u) << 15) | BitNumIntL(state, 20, 5) |
                BitNumIntL(state, 19, 6) | ((state & 0x00000f00u) << 13) | BitNumIntL(state, 24, 11) |
                BitNumIntL(state, 23, 12) | ((state & 0x000000f0u) << 11) | BitNumIntL(state, 28, 17) |
                BitNumIntL(state, 27, 18) | ((state & 0x0000000fu) << 9) | BitNumIntL(state, 0, 23);

            lrgstate[0] = (byte)(t1 >> 24);
            lrgstate[1] = (byte)(t1 >> 16);
            lrgstate[2] = (byte)(t1 >> 8);
            lrgstate[3] = (byte)(t2 >> 24);
            lrgstate[4] = (byte)(t2 >> 16);
            lrgstate[5] = (byte)(t2 >> 8);

            for (int i = 0; i < 6; i++) lrgstate[i] ^= key[i];

            state =
                ((uint)SBox1[SBoxBit(lrgstate[0] >> 2)] << 28) |
                ((uint)SBox2[SBoxBit(((lrgstate[0] & 0x03) << 4) | (lrgstate[1] >> 4))] << 24) |
                ((uint)SBox3[SBoxBit(((lrgstate[1] & 0x0f) << 2) | (lrgstate[2] >> 6))] << 20) |
                ((uint)SBox4[SBoxBit(lrgstate[2] & 0x3f)] << 16) |
                ((uint)SBox5[SBoxBit(lrgstate[3] >> 2)] << 12) |
                ((uint)SBox6[SBoxBit(((lrgstate[3] & 0x03) << 4) | (lrgstate[4] >> 4))] << 8) |
                ((uint)SBox7[SBoxBit(((lrgstate[4] & 0x0f) << 2) | (lrgstate[5] >> 6))] << 4) |
                (uint)SBox8[SBoxBit(lrgstate[5] & 0x3f)];

            state =
                BitNumIntL(state, 15, 0) | BitNumIntL(state, 6, 1) | BitNumIntL(state, 19, 2) |
                BitNumIntL(state, 20, 3) | BitNumIntL(state, 28, 4) | BitNumIntL(state, 11, 5) |
                BitNumIntL(state, 27, 6) | BitNumIntL(state, 16, 7) | BitNumIntL(state, 0, 8) |
                BitNumIntL(state, 14, 9) | BitNumIntL(state, 22, 10) | BitNumIntL(state, 25, 11) |
                BitNumIntL(state, 4, 12) | BitNumIntL(state, 17, 13) | BitNumIntL(state, 30, 14) |
                BitNumIntL(state, 9, 15) | BitNumIntL(state, 1, 16) | BitNumIntL(state, 7, 17) |
                BitNumIntL(state, 23, 18) | BitNumIntL(state, 13, 19) | BitNumIntL(state, 31, 20) |
                BitNumIntL(state, 26, 21) | BitNumIntL(state, 2, 22) | BitNumIntL(state, 8, 23) |
                BitNumIntL(state, 18, 24) | BitNumIntL(state, 12, 25) | BitNumIntL(state, 29, 26) |
                BitNumIntL(state, 5, 27) | BitNumIntL(state, 21, 28) | BitNumIntL(state, 10, 29) |
                BitNumIntL(state, 3, 30) | BitNumIntL(state, 24, 31);

            return state;
        }

        private static void DesKeySetup(byte[] key, byte[][] schedule, DesMode mode)
        {
            uint c = 0, d = 0;
            for (int i = 0; i < 28; i++)
            {
                c |= BitNum(key, KeyPermC[i], 31 - i);
                d |= BitNum(key, KeyPermD[i], 31 - i);
            }

            for (int i = 0; i < 16; i++)
            {
                int shift = KeyRndShift[i];
                c = ((c << shift) | (c >> (28 - shift))) & 0xfffffff0u;
                d = ((d << shift) | (d >> (28 - shift))) & 0xfffffff0u;
                int toGen = mode == DesMode.Decrypt ? 15 - i : i;
                schedule[toGen] = new byte[6];
                for (int j = 0; j < 24; j++)
                    schedule[toGen][j / 8] |= (byte)BitNumIntR(c, KeyCompression[j], 7 - j % 8);
                for (int j = 24; j < 48; j++)
                    schedule[toGen][j / 8] |= (byte)BitNumIntR(d, KeyCompression[j] - 27, 7 - j % 8);
            }
        }

        private static void DesCrypt(byte[] inputBytes, byte[][] keySchedule)
        {
            var state = new uint[2];
            IpPermutation(state, inputBytes);
            for (int idx = 0; idx < 15; idx++)
            {
                uint t = state[1];
                state[1] = FeistelF(state[1], keySchedule[idx]) ^ state[0];
                state[0] = t;
            }
            state[0] = FeistelF(state[1], keySchedule[15]) ^ state[0];
            InvIp(state, inputBytes);
        }

        /// <summary>按指定密钥表模式(KEY1 解密→KEY2 加密→KEY3 解密)逐 8 字节块处理。</summary>
        private static byte[] RunDes(byte[] buff, byte[] key, DesMode mode, int length)
        {
            var schedule = new byte[16][];
            DesKeySetup(key, schedule, mode);
            var output = new byte[length];
            for (int i = 0; i + 8 <= length; i += 8)
            {
                var block = new byte[8];
                Array.Copy(buff, i, block, 0, 8);
                DesCrypt(block, schedule);
                Array.Copy(block, 0, output, i, 8);
            }
            return output;
        }

        /// <summary>QRC Triple-DES: KEY1 解密 → KEY2 加密 → KEY3 解密。</summary>
        private static byte[] LyricDecode(byte[] content)
        {
            int length = content.Length;
            var result = RunDes(content, Key1, DesMode.Decrypt, length);
            result = RunDes(result, Key2, DesMode.Encrypt, length);
            result = RunDes(result, Key3, DesMode.Decrypt, length);
            return result;
        }

        // ============ 公开 API ============

        /// <summary>
        /// 自动解密歌词: 输入为 QRC 加密 hex 时解密(含 QRC XML 转标准 LRC), 否则原样返回。
        /// </summary>
        public static string? AutoDecrypt(string? lyric)
        {
            if (string.IsNullOrEmpty(lyric)) return lyric;
            var trimmed = lyric.Trim();
            if (IsQrcEncrypted(trimmed))
            {
                try
                {
                    var bytes = HexToBytes(trimmed);
                    if (bytes is null) return lyric;
                    var decrypted = LyricDecode(bytes);
                    var inflated = SafeInflate(decrypted);
                    if (inflated is null) return lyric;
                    var text = Encoding.UTF8.GetString(inflated);
                    return IsQrcXml(text) ? ConvertQrcXmlToLrc(text) : text;
                }
                catch
                {
                    return lyric;
                }
            }
            if (IsQrcXml(trimmed)) return ConvertQrcXmlToLrc(trimmed);
            return lyric;
        }

        /// <summary>判断是否为 QRC 加密 hex: 长度 16 倍数、纯十六进制且不含 LRC 时间标签。</summary>
        private static bool IsQrcEncrypted(string s)
        {
            if (s.Length < 32 || s.Length % 16 != 0) return false;
            foreach (var ch in s)
                if (!Uri.IsHexDigit(ch)) return false;
            if (Regex.IsMatch(s, @"\[\d{2}:\d{2}[.:]\d{2,3}\]")) return false;
            return true;
        }

        private static bool IsQrcXml(string text) => text.Contains("<?xml") && text.Contains("LyricContent");

        // KRC 行首段标记(标准多行形态)与段内标记(单行形态), 用于单行 KRC 修复
        private static readonly Regex KrcLineStartRegex =
            new(@"^\[\d+,\d+\]", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex KrcSegmentRegex =
            new(@"\[\d+,\d+\]", RegexOptions.Compiled);

        /// <summary>
        /// 修复单行 KRC: QQ 接口歌词经 QRC XML 属性提取后换行丢失(XDocument 解析会把属性内的
        /// 换行规范化为空格), 全部 [start,dur] 段落挤在一行以空格分隔。
        /// 在每个 [start,dur] 段前重建换行恢复多行 KRC; 首段前的 [ti:] 等头部元数据保留为独立行。
        /// 已是标准多行 KRC 或不含段标记时原样返回。
        /// </summary>
        public static string NormalizeInlineKrc(string content)
        {
            if (string.IsNullOrEmpty(content)) return content;
            if (KrcLineStartRegex.IsMatch(content)) return content;

            var matches = KrcSegmentRegex.Matches(content);
            if (matches.Count == 0) return content;

            var sb = new StringBuilder(content.Length + matches.Count);
            for (int i = 0; i < matches.Count; i++)
            {
                if (i == 0)
                {
                    var header = content.AsSpan(0, matches[i].Index).Trim();
                    if (!header.IsEmpty)
                    {
                        sb.Append(header.ToString());
                        sb.Append('\n');
                    }
                }
                else
                {
                    sb.Append('\n');
                }
                int end = i + 1 < matches.Count ? matches[i + 1].Index : content.Length;
                sb.Append(content, matches[i].Index, end - matches[i].Index);
            }
            return sb.ToString();
        }

        private static byte[]? HexToBytes(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }

        /// <summary>zlib 解压(跳过 2 字节 zlib 头用 Deflate 流, 容忍尾部填充), 输出上限 5MB。</summary>
        private static byte[]? SafeInflate(byte[] data)
        {
            if (data.Length is < 2 or > 2 * 1024 * 1024) return null;
            try
            {
                int offset = data[0] == 0x78 ? 2 : 0;
                using var output = new MemoryStream();
                using (var src = new MemoryStream(data, offset, data.Length - offset))
                using (var deflate = new DeflateStream(src, CompressionMode.Decompress))
                    deflate.CopyTo(output);
                var result = output.ToArray();
                return result.Length <= 5 * 1024 * 1024 ? result : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// QRC XML 转标准 LRC。LyricContent 属性为多行文本:
        /// 头部标签行([ti:] 等)原样保留, 歌词行 [start,dur]词(起,长)... → [mm:ss.xx]词词... (剥离逐字时间)。
        /// </summary>
        private static string ConvertQrcXmlToLrc(string xml)
        {
            var lines = new List<string>();
            IEnumerable<string> contents;
            try
            {
                contents = XDocument.Parse(xml).Descendants()
                    .Select(e => (string?)e.Attribute("LyricContent"))
                    .Where(v => !string.IsNullOrEmpty(v))!;
            }
            catch
            {
                // XML 非法(如未转义 & )时退化为正则提取属性
                contents = Regex.Matches(xml, "LyricContent\\s*=\\s*\"([^\"]*)\"")
                    .Select(m => UnescapeXml(m.Groups[1].Value));
            }

            foreach (var content in contents)
            {
                // XDocument 属性规范化会把 LyricContent 内的换行替换为空格,
                // 先按 [start,dur] 段重建多行, 否则整首歌词挤成单行无法解析
                foreach (var rawLine in NormalizeInlineKrc(content).Replace("\r\n", "\n").Split('\n'))
                {
                    var line = rawLine.TrimEnd();
                    if (line.Length == 0) continue;
                    var m = Regex.Match(line, @"^\[(\d+),(\d+)\]");
                    if (!m.Success)
                    {
                        // [ti:]/[ar:] 等头部标签行原样保留
                        lines.Add(line);
                        continue;
                    }
                    var ms = long.Parse(m.Groups[1].Value);
                    var text = Regex.Replace(line, @"^\[\d+,\d+\]", "");
                    text = Regex.Replace(text, @"\(\d+,\d+\)", "");
                    text = Regex.Replace(text, @"\[kana:.*?\]", "");
                    if (text.Trim().Length == 0) continue;
                    var timestamp = $"[{ms / 60000:D2}:{ms / 1000 % 60:D2}.{ms / 10 % 100:D2}]";
                    lines.Add(timestamp + text.TrimEnd());
                }
            }
            return string.Join("\r\n", lines);
        }

        private static string UnescapeXml(string s) => s
            .Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"")
            .Replace("&apos;", "'").Replace("&amp;", "&");
    }
}
