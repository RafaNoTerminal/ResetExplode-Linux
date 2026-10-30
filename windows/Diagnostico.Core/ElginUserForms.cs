using System.Buffers.Binary;
using System.Text;
namespace Diagnostico;

// Exact driver's UserForm REG_BINARY: 92-byte records, as written by its Manage UI.
public static class ElginUserForms
{
    public const int RecordSize=92;
    public static byte[] EnsureReceipt(byte[]? existing) {
        var data=existing ?? [];
        if(data.Length%RecordSize!=0 || data.Length>1048576) throw new IOException("A lista de papéis Elgin tem formato desconhecido; preservada.");
        int matches=0;
        for(int i=0;i<data.Length;i+=RecordSize) {
            string name=Encoding.Unicode.GetString(data,i+4,64).Split('\0')[0];
            if(!name.Equals("Comprovantes",StringComparison.OrdinalIgnoreCase))continue;
            matches++;
            if(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(i+68))!=72000 || BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(i+72))!=100000)throw new IOException("O driver Elgin já tem papel Comprovantes com outro tamanho; preservado.");
        }
        if(matches>1)throw new IOException("Papéis Comprovantes duplicados no driver; lista preservada.");
        if(matches==1)return (byte[])data.Clone();
        var result=new byte[data.Length+RecordSize];data.CopyTo(result,0);
        Encoding.Unicode.GetBytes("Comprovantes").CopyTo(result,data.Length+4);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(data.Length+68),72000);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(data.Length+72),100000);
        // The genuine Manage UI creates a zero RECT; preserve that representation.
        return result;
    }
    public static ushort ResolveReceipt((ushort Id,string Name,int Width,int Height)[] papers) {
        var matches=papers.Where(p=>p.Name.Equals("Comprovantes",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(matches.Length!=1 || matches[0].Id<=256 || matches[0].Width!=720 || matches[0].Height!=1000)throw new IOException("O driver não anunciou o papel Comprovantes 72×100 mm com identificador próprio. Nenhum ajuste foi confirmado.");
        return matches[0].Id;
    }
}
