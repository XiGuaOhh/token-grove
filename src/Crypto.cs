using System;
using System.Security.Cryptography;
using System.Text;

namespace TokenGrove {
  // AES-GCM verification for the current Windows user's Chromium safeStorage.
  // No credential is refreshed, serialized, logged, or sent outside Anthropic.
  public static class LocalCrypto {
    static byte[] Xor(byte[] a,byte[] b){var o=new byte[16];for(int i=0;i<16;i++)o[i]=(byte)(a[i]^b[i]);return o;}
    static byte[] Multiply(byte[] x,byte[] y){var z=new byte[16];var v=(byte[])y.Clone();for(int i=0;i<128;i++){if((x[i/8]&(1<<(7-i%8)))!=0)for(int k=0;k<16;k++)z[k]^=v[k];bool low=(v[15]&1)!=0;for(int k=15;k>=0;k--)v[k]=(byte)((v[k]>>1)|(k>0?(v[k-1]&1)<<7:0));if(low)v[0]^=0xe1;}return z;}
    static void Length(byte[] b,int offset,ulong bits){for(int i=7;i>=0;i--){b[offset+i]=(byte)(bits&255);bits>>=8;}}
    public static byte[] DecryptGcm(byte[] key,byte[] nonce,byte[] cipher,byte[] tag){
      if(nonce.Length!=12||tag.Length!=16)throw new CryptographicException("Unsupported authenticated payload");
      using(var aes=Aes.Create()){aes.Key=key;aes.Mode=CipherMode.ECB;aes.Padding=PaddingMode.None;using(var enc=aes.CreateEncryptor()){
        Func<byte[],byte[]> block=b=>{var r=new byte[16];enc.TransformBlock(b,0,16,r,0);return r;};
        var h=block(new byte[16]);var s=new byte[16];for(int p=0;p<cipher.Length;p+=16){var b=new byte[16];Buffer.BlockCopy(cipher,p,b,0,Math.Min(16,cipher.Length-p));s=Multiply(Xor(s,b),h);}
        var lengths=new byte[16];Length(lengths,8,(ulong)cipher.Length*8);s=Multiply(Xor(s,lengths),h);
        var counter=new byte[16];Buffer.BlockCopy(nonce,0,counter,0,12);counter[15]=1;var expected=Xor(block(counter),s);int difference=0;for(int i=0;i<16;i++)difference|=expected[i]^tag[i];if(difference!=0)throw new CryptographicException("Authentication failed");
        var output=new byte[cipher.Length];for(int p=0;p<cipher.Length;p+=16){for(int i=15;i>=12;i--){counter[i]++;if(counter[i]!=0)break;}var pad=block(counter);for(int i=0;i<Math.Min(16,cipher.Length-p);i++)output[p+i]=(byte)(cipher[p+i]^pad[i]);}return output;
      }}
    }
    public static string DecryptSafeStorage(string base64,string encryptedKey){
      var blob=Convert.FromBase64String(base64);byte[] plain=null,key=null;
      try{if(blob.Length>31&&blob[0]=='v'&&blob[1]=='1'&&(blob[2]=='0'||blob[2]=='1')){
        var wrapped=Convert.FromBase64String(encryptedKey);if(wrapped.Length<6||Encoding.ASCII.GetString(wrapped,0,5)!="DPAPI")throw new CryptographicException("Unsupported local key format");
        var dpapi=new byte[wrapped.Length-5];Buffer.BlockCopy(wrapped,5,dpapi,0,dpapi.Length);key=ProtectedData.Unprotect(dpapi,null,DataProtectionScope.CurrentUser);
        var nonce=new byte[12];Buffer.BlockCopy(blob,3,nonce,0,12);var cipher=new byte[blob.Length-31];Buffer.BlockCopy(blob,15,cipher,0,cipher.Length);var tag=new byte[16];Buffer.BlockCopy(blob,blob.Length-16,tag,0,16);plain=DecryptGcm(key,nonce,cipher,tag);
      }else plain=ProtectedData.Unprotect(blob,null,DataProtectionScope.CurrentUser);return Encoding.UTF8.GetString(plain);
      }finally{if(key!=null)Array.Clear(key,0,key.Length);if(plain!=null)Array.Clear(plain,0,plain.Length);Array.Clear(blob,0,blob.Length);}
    }
  }
}
