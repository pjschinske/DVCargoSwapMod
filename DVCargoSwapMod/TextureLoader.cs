using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace DVCargoSwapMod
{
    public static class TextureLoader
    {
        public static Task<Texture2D> Add(FileInfo fileInfo, bool linear)
        {
            var result = TryLoadFromCache(fileInfo, linear);
            if (!result.IsCompleted || result.Result != null)
                return result;
            return Load(fileInfo, linear);
        }

        private static Task<Texture2D> TryLoadFromCache(FileInfo fileInfo, bool linear)
        {
            var cached = new FileInfo(GetCachePath(fileInfo.FullName));
            if (!cached.Exists)
                return Task.FromResult<Texture2D>(null);
            if (cached.LastWriteTimeUtc < fileInfo.LastWriteTimeUtc)
            {
                cached.Delete();
                return Task.FromResult<Texture2D>(null);
            }

            return DDSUtils.ReadDDSGz(cached, linear);
        }

        private static Task<Texture2D> Load(FileInfo fileInfo, bool isNormalMap)
        {
            var info = StbImage.GetImageInfo(fileInfo.FullName);
            var format = isNormalMap ? TextureFormat.BC5 :
                info.componentCount > 3 ? TextureFormat.DXT5 :
                TextureFormat.DXT1;
            var texture = new Texture2D(info.width, info.height, format,
                mipChain: true, linear: isNormalMap);
            var nativeArray = texture.GetRawTextureData<byte>();
            return Task.Run(() =>
            {
                PopulateTexture(fileInfo, format, nativeArray);
                var cachePath = GetCachePath(fileInfo.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
                DDSUtils.WriteDDSGz(new FileInfo(cachePath), texture);
                return texture;
            });
        }

        private static string GetCachePath(string path)
        {
            var sep = Path.DirectorySeparatorChar;
            var cacheDirName = Path.GetDirectoryName(
                path.Replace(sep + "Skins" + sep, sep + "Cache" + sep)
                .Replace(sep + "SkinsAC" + sep, sep + "CacheAC" + sep)
                );
            var cacheFileName = Path.GetFileNameWithoutExtension(path) + ".dds.gz";
            return Path.Combine(cacheDirName, cacheFileName);
        }

        private static void PopulateTexture(FileInfo path, TextureFormat textureFormat, NativeArray<byte> dest)
        {
            StbImage.TextureFormat format;
            switch (textureFormat)
            {
                case TextureFormat.DXT1:
                    format = StbImage.TextureFormat.BC1;
                    break;
                case TextureFormat.DXT5:
                    format = StbImage.TextureFormat.BC3;
                    break;
                case TextureFormat.BC5:
                    format = StbImage.TextureFormat.BC5;
                    break;
                default:
                    throw new ArgumentException("textureFormat", $"Unsupported TextureFormat {textureFormat}");
            }

            unsafe
            {
                StbImage.ReadAndCompressImageWithMipmaps(
                    path.FullName,
                    flipVertically: true,
                    format: format,
                    (IntPtr)dest.GetUnsafePtr(),
                    dest.Length);
            }
        }
    }

    internal static class DDSUtils
    {
        private static int Mipmap0SizeInBytes(int width, int height, UnityEngine.TextureFormat textureFormat)
        {
            var blockWidth = (width + 3) / 4;
            var blockHeight = (height + 3) / 4;
            int bytesPerBlock;
            switch (textureFormat)
            {
                case TextureFormat.DXT1:
                    bytesPerBlock = 8;
                    break;
                case TextureFormat.DXT5:
                case TextureFormat.BC5:
                    bytesPerBlock = 16;
                    break;
                default:
                    throw new ArgumentException("textureFormat", $"Unsupported TextureFormat {textureFormat}");
            }
            return blockWidth * blockHeight * bytesPerBlock;
        }

        private const int DDS_HEADER_SIZE = 128;
        private const int DDS_HEADER_DXT10_SIZE = 20;
        private static byte[] DDSHeader(int width, int height, TextureFormat textureFormat, int numMipmaps)
        {
            var needsDXGIHeader = textureFormat != TextureFormat.DXT1 && textureFormat != TextureFormat.DXT5;
            var headerSize = needsDXGIHeader ? DDS_HEADER_SIZE + DDS_HEADER_DXT10_SIZE : DDS_HEADER_SIZE;
            var header = new byte[headerSize];
            using (var stream = new MemoryStream(header))
            {
                stream.Write(Encoding.ASCII.GetBytes("DDS "), 0, 4);
                stream.Write(BitConverter.GetBytes(124), 0, 4); // dwSize
                                                                // dwFlags = CAPS | HEIGHT | WIDTH | PIXELFORMAT | MIPMAPCOUNT | LINEARSIZE
                stream.Write(BitConverter.GetBytes(0x1 | 0x2 | 0x4 | 0x1000 | 0x20000 | 0x80000), 0, 4);
                stream.Write(BitConverter.GetBytes(height), 0, 4);
                stream.Write(BitConverter.GetBytes(width), 0, 4);
                stream.Write(BitConverter.GetBytes(Mipmap0SizeInBytes(width, height, textureFormat)), 0, 4); // dwPitchOrLinearSize
                stream.Write(BitConverter.GetBytes(0), 0, 4); // dwDepth
                stream.Write(BitConverter.GetBytes(numMipmaps), 0, 4); // dwMipMapCount
                for (int i = 0; i < 11; i++)
                    stream.Write(BitConverter.GetBytes(0), 0, 4); // dwReserved1
                var pixelFormat = PixelFormat(textureFormat);
                stream.Write(pixelFormat, 0, pixelFormat.Length);
                // dwCaps = COMPLEX | MIPMAP | TEXTURE
                stream.Write(BitConverter.GetBytes(0x401008), 0, 4);

                if (needsDXGIHeader)
                    stream.Write(DDSHeaderDXT10(textureFormat), 0, DDS_HEADER_DXT10_SIZE);
            }
            return header;
        }

        private static byte[] DDSHeaderDXT10(TextureFormat textureFormat)
        {
            var headerDXT10 = new byte[DDS_HEADER_DXT10_SIZE];
            using (var stream = new MemoryStream(headerDXT10))
            {
                stream.Write(BitConverter.GetBytes(DXGIFormat(textureFormat)), 0, 4); // dxgiFormat
                stream.Write(BitConverter.GetBytes(3), 0, 4); // resourceDimension = 3 = DDS_DIMENSION_TEXTURE2D
                stream.Write(BitConverter.GetBytes(0), 0, 4); // miscFlag
                stream.Write(BitConverter.GetBytes(1), 0, 4); // arraySize = 1
                stream.Write(BitConverter.GetBytes(0), 0, 4); // miscFlags2 = 0 = DDS_ALPHA_MODE_UNKNOWN
            }
            return headerDXT10;
        }

        private static int DXGIFormat(TextureFormat textureFormat)
        {
            switch (textureFormat)
            {
                case TextureFormat.BC5: return 83;
                default:
                    throw new ArgumentException("textureFormat", $"Unsupported TextureFormat {textureFormat}");
            }
        }

        private static byte[] PixelFormat(TextureFormat textureFormat)
        {
            string fourCC;
            switch (textureFormat)
            {
                case TextureFormat.DXT1:
                    fourCC = "DXT1";
                    break;
                case TextureFormat.DXT5:
                    fourCC = "DXT5";
                    break;
                default:
                    fourCC = "DX10";
                    break;
            }

            var pixelFormat = new byte[32];
            using (var stream = new MemoryStream(pixelFormat))
            {
                stream.Write(BitConverter.GetBytes(32), 0, 4); // dwSize
                stream.Write(BitConverter.GetBytes(0x4), 0, 4); // dwFlags = FOURCC
                stream.Write(Encoding.ASCII.GetBytes(fourCC), 0, 4); // dwFourCC
            }
            return pixelFormat;
        }

        public static void WriteDDSGz(FileInfo fileInfo, Texture2D texture)
        {
            var outfile = new GZipStream(fileInfo.OpenWrite(), CompressionLevel.Optimal);
            var header = DDSHeader(texture.width, texture.height, texture.format, texture.mipmapCount);
            outfile.Write(header, 0, header.Length);
            var data = texture.GetRawTextureData<byte>().ToArray();
            Debug.Log($"Writing to {fileInfo.FullName}");
            outfile.Write(data, 0, data.Length);
            outfile.Close();
        }

        public static Task<Texture2D> ReadDDSGz(FileInfo fileInfo, bool linear)
        {
            var infile = new GZipStream(fileInfo.OpenRead(), CompressionMode.Decompress);
            var buf = new byte[4096];
            var bytesRead = infile.Read(buf, 0, 128);
            if (bytesRead != 128 || Encoding.ASCII.GetString(buf, 0, 4) != "DDS ")
                throw new Exception("File is not a DDS file");

            int height = BitConverter.ToInt32(buf, 12);
            int width = BitConverter.ToInt32(buf, 16);

            int pixelFormatFlags = BitConverter.ToInt32(buf, 80);
            if ((pixelFormatFlags & 0x4) == 0)
                throw new Exception("DDS header does not have a FourCC");
            string fourCC = Encoding.ASCII.GetString(buf, 84, 4);
            TextureFormat pixelFormat;
            switch (fourCC)
            {
                case "DXT1": pixelFormat = TextureFormat.DXT1; break;
                case "DXT5": pixelFormat = TextureFormat.DXT5; break;
                case "DX10":
                    // read DDS_HEADER_DXT10 header extension
                    bytesRead = infile.Read(buf, 0, DDS_HEADER_DXT10_SIZE);
                    if (bytesRead != DDS_HEADER_DXT10_SIZE)
                        throw new Exception("Could not read DXT10 header from DDS file");
                    int dxgiFormat = BitConverter.ToInt32(buf, 0);
                    switch (dxgiFormat)
                    {
                        case 83:
                            pixelFormat = TextureFormat.BC5;
                            break;
                        default:
                            throw new Exception($"Unsupported DXGI_FORMAT {dxgiFormat}");
                    }
                    break;
                default    :  throw new Exception($"Unknown FourCC: {fourCC}");
            }

            var texture = new Texture2D(width, height, pixelFormat, true, linear);
            var nativeArray = texture.GetRawTextureData<byte>();
            return Task.Run(() =>
            {
                buf = new byte[nativeArray.Length];
                bytesRead = infile.Read(buf, 0, nativeArray.Length);
                if (bytesRead < nativeArray.Length)
                    throw new Exception($"{fileInfo.FullName}: Expected {nativeArray.Length} bytes, but file contained {bytesRead}");
                nativeArray.CopyFrom(buf);
                return texture;
            });
        }
    }
}