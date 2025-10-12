using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

public static class FileHelper {
    public static void WriteString(string path, string input, bool doEncrypt) {
        path = $"{Application.persistentDataPath}/{path}.json";
        if (doEncrypt) {
            input = EncryptionHelper.Encrypt(input);
        }
        File.WriteAllText(path, input);
    }

    public static string ReadString(string path, bool isEncrypted) {
        try {
            path = $"{Application.persistentDataPath}/{path}.json";
            string output = File.ReadAllText(path);
            if(isEncrypted) {
                output = EncryptionHelper.Decrypt(output);
            }
            return output;
        } catch {
#if !UNITY_EDITOR
            Debug.LogWarning($"Could not find / read string. {path}");
#endif
            return null;
        }
    }

    public static string Compress(string data) {
        byte[] bytes = Encoding.UTF8.GetBytes(data);
        using var memoryStream = new MemoryStream();
        using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress)) {
            gzipStream.Write(bytes, 0, bytes.Length);
        }
        return Convert.ToBase64String(memoryStream.ToArray());
    }

    public static string Decompress(string compressedData) {
        byte[] bytes = Convert.FromBase64String(compressedData);
        using var memoryStream = new MemoryStream(bytes);
        using var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzipStream);
        return reader.ReadToEnd();
    }
}