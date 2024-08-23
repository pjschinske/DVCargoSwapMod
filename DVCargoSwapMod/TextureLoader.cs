using Microsoft.SqlServer.Server;
using System;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using static DVCargoSwapMod.StbImage;

namespace DVCargoSwapMod
{
    public static class TextureLoader
    {
        public static Task<Texture2D> Add(FileInfo fileInfo, bool linear, bool resize)
        {
            var result = TryLoadFromCache(fileInfo, linear);
            if (!result.IsCompleted || result.Result != null)
                return result;
            return Load(fileInfo, linear, resize);
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

        private static Task<Texture2D> Load(FileInfo fileInfo, bool isNormalMap, bool resize)
        {
            Main.mod.Logger.Log($"fileInfo.FullName = '{fileInfo.FullName}'");
            var info = StbImage.GetImageInfo(fileInfo.FullName);
            var format = isNormalMap ? UnityEngine.TextureFormat.BC5 :
                info.componentCount > 3 ? UnityEngine.TextureFormat.DXT5 :
                UnityEngine.TextureFormat.DXT1;
            Texture2D texture = new Texture2D(info.width, info.height, format,
                    mipChain: true, linear: isNormalMap);

            if (resize)
            {
                //Now we need to resize the texture to be only 2K by 2K instead of 8K by 8K
                //We only want to resize the 40ft container skins
                /*Main.mod.Logger.Log($"Resizing texture {fileInfo.Name} " +
                    $"with format {format} " +
                    $"with size {info.width}x{info.height} " +
                    $"to {info.width / 4}x{info.height / 4}");*/

                //TODO: figure out a way to copy in the bytes we need. Might have to do this manually
                //(i.e., get both buffers and copy over the correct bytes)

                //maybe:
                //1. read as RGBA
                //2. resize
                //3. compress resized texture
                //4. cache resized texture
                //5. use resized texture

                //or use this:
                //https://stackoverflow.com/questions/51315918/how-to-encodetopng-compressed-textures-in-unity
                var nativeArray = texture.GetRawTextureData<byte>();
                PopulateTexture(fileInfo, format, nativeArray);

                Texture2D littleTexture = ResizeTexture(texture, format);

                info.width = littleTexture.width;
                info.height = littleTexture.height;
                texture = littleTexture;
            }
            else
            {
                var nativeArray = texture.GetRawTextureData<byte>();
                PopulateTexture(fileInfo, format, nativeArray);
            }

            //TODO: all loaded in textures seem to be blank, and loading in takes forever

            return Task.Run(() =>
            {
                

                var cachePath = GetCachePath(fileInfo.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
                DDSUtils.WriteDDSGz(new FileInfo(cachePath), texture);
                return texture;
            });
        }

        //From https://stackoverflow.com/questions/44733841/how-to-make-texture2d-readable-via-script/44734346#44734346
        internal static Texture2D DuplicateTexture(Texture2D source,
            RenderTextureFormat format,
            RenderTextureReadWrite colorSpace,
            int width, int height)
        {

            RenderTexture renderTex = RenderTexture.GetTemporary(
                        source.width,
                        source.height,
                        0,
                        format,
                        colorSpace);

            Graphics.Blit(source, renderTex);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTex;
            Texture2D editableTexture = new Texture2D(width, height);
            //IDK if this is getting the top left or bottom left of the texture.
            //Luckily, they're the same for the 40 ft container shader texture (ContainersAtlas_01s)
            editableTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            editableTexture.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTex);
            return editableTexture;
        }

        internal static Texture2D ResizeTexture(Texture2D texture, UnityEngine.TextureFormat format)
        {
            if (texture == null)
            {
                return null;
            }

            var nativeArray = texture.GetRawTextureData<byte>();
            
            UnityEngine.TextureFormat littleFormat = 0;
            switch (format)
            {
                case UnityEngine.TextureFormat.DXT1:
                    littleFormat = UnityEngine.TextureFormat.RGB24;
                    break;
                case UnityEngine.TextureFormat.DXT5:
                    littleFormat = UnityEngine.TextureFormat.RGBA32;
                    break;
                case UnityEngine.TextureFormat.BC5:
                    littleFormat = UnityEngine.TextureFormat.RGB24;
                    break;
                default:
                    throw new ArgumentException("textureFormat", $"Unsupported TextureFormat {format} when resizing");
            }

            int oldWidth = texture.width,
                oldHeight = texture.height;
            int width = oldWidth / 4,
                height = oldHeight / 4;
            Texture2D littleTexture = new(oldWidth / 4, oldHeight / 4, littleFormat,
                mipChain: true, linear: false);
            littleTexture.LoadRawTextureData(nativeArray);
            Color[] mipmapData;
            for (int currentMipmapLevel = 0; currentMipmapLevel < texture.mipmapCount; currentMipmapLevel++)
            {
                if (width == 0 || height == 0)
                {
                    break;
                }
                mipmapData = texture.GetPixels(0, height * 3 - 1, width, height, currentMipmapLevel);
                //Main.mod.Logger.Log($"width: {width}, height: {height}, mipmap level: {currentMipmapLevel}, # of pixels: {mipmapData.Length}.");
                littleTexture.SetPixels(mipmapData, currentMipmapLevel);
                width /= 2;
                height /= 2;
            }

            littleTexture.Compress(false);

            return littleTexture;

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

        private static void PopulateTexture(FileInfo path, UnityEngine.TextureFormat textureFormat, NativeArray<byte> dest)
        {
            StbImage.TextureFormat format;
            switch (textureFormat)
            {
                case UnityEngine.TextureFormat.DXT1:
                    format = StbImage.TextureFormat.BC1;
                    break;
                case UnityEngine.TextureFormat.DXT5:
                    format = StbImage.TextureFormat.BC3;
                    break;
                case UnityEngine.TextureFormat.BC5:
                    format = StbImage.TextureFormat.BC5;
                    break;
                default:
                    throw new ArgumentException("textureFormat", $"Unsupported TextureFormat {textureFormat}");
            }
            //Main.mod.Logger.Log($"path.FullName = '{path.FullName}'");
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
                case UnityEngine.TextureFormat.DXT1:
                    bytesPerBlock = 8;
                    break;
                case UnityEngine.TextureFormat.DXT5:
                case UnityEngine.TextureFormat.BC5:
                    bytesPerBlock = 16;
                    break;
                default:
                    throw new ArgumentException("textureFormat", $"Unsupported TextureFormat {textureFormat}");
            }
            return blockWidth * blockHeight * bytesPerBlock;
        }

        private const int DDS_HEADER_SIZE = 128;
        private const int DDS_HEADER_DXT10_SIZE = 20;
        private static byte[] DDSHeader(int width, int height, UnityEngine.TextureFormat textureFormat, int numMipmaps)
        {
            var needsDXGIHeader = textureFormat != UnityEngine.TextureFormat.DXT1 && textureFormat != UnityEngine.TextureFormat.DXT5;
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

        private static byte[] DDSHeaderDXT10(UnityEngine.TextureFormat textureFormat)
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

        private static int DXGIFormat(UnityEngine.TextureFormat textureFormat)
        {
            switch (textureFormat)
            {
                case UnityEngine.TextureFormat.BC5: return 83;
                default:
                    throw new ArgumentException("textureFormat", $"Unsupported TextureFormat {textureFormat}");
            }
        }

        private static byte[] PixelFormat(UnityEngine.TextureFormat textureFormat)
        {
            string fourCC;
            switch (textureFormat)
            {
                case UnityEngine.TextureFormat.DXT1:
                    fourCC = "DXT1";
                    break;
                case UnityEngine.TextureFormat.DXT5:
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
            //Debug.Log($"Writing to {fileInfo.FullName}");
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
            UnityEngine.TextureFormat pixelFormat;
            switch (fourCC)
            {
                case "DXT1": pixelFormat = UnityEngine.TextureFormat.DXT1; break;
                case "DXT5": pixelFormat = UnityEngine.TextureFormat.DXT5; break;
                case "DX10":
                    // read DDS_HEADER_DXT10 header extension
                    bytesRead = infile.Read(buf, 0, DDS_HEADER_DXT10_SIZE);
                    if (bytesRead != DDS_HEADER_DXT10_SIZE)
                        throw new Exception("Could not read DXT10 header from DDS file");
                    int dxgiFormat = BitConverter.ToInt32(buf, 0);
                    switch (dxgiFormat)
                    {
                        case 83:
                            pixelFormat = UnityEngine.TextureFormat.BC5;
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