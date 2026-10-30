using System.ComponentModel;
namespace Diagnostico;
public static class ReceiptPaperForm
{
    public static void Ensure(Func<(int Width,int Height)> read, Action create) {
        (int Width,int Height) size;
        try { size=read(); }
        catch (Win32Exception e) when (e.NativeErrorCode is 1902 or 2) {
            create();
            size=read();
        }
        if(size.Width!=72000 || size.Height!=100000) throw new IOException("O papel Comprovantes existe com tamanho diferente de 72×100 mm. O formato existente foi preservado.");
    }
}
