using System.Text;

public static class EncryptionHelper {
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("COMPUTER_SPOOK_SAYS_HELLO!");

    public static string Encrypt(string data) {
        byte[] dataBytes = Encoding.UTF8.GetBytes(data);
        byte[] encryptedBytes = new byte[dataBytes.Length];

        for (int i = 0; i < dataBytes.Length; i++) {
            encryptedBytes[i] = (byte)(dataBytes[i] ^ Key[i % Key.Length]);
        }

        return System.Convert.ToBase64String(encryptedBytes);
    }

    public static string Decrypt(string data) {
        byte[] encryptedBytes = System.Convert.FromBase64String(data);
        byte[] decryptedBytes = new byte[encryptedBytes.Length];

        for (int i = 0; i < encryptedBytes.Length; i++) {
            decryptedBytes[i] = (byte)(encryptedBytes[i] ^ Key[i % Key.Length]);
        }

        return Encoding.UTF8.GetString(decryptedBytes);
    }
}