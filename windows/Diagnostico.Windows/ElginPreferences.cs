using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Diagnostico.Windows;

internal record ReceiptQueueBackup(int Format, string Computer, string Queue, string Driver, string Fingerprint, byte[] Global, byte[]? User, DateTimeOffset SavedAt, byte[]? DriverForms = null);
internal sealed class ElginPreferences : IDisposable
{
    readonly IntPtr handle; readonly string queue;
    internal string Driver { get; }
    internal string Fingerprint { get; }
    internal string VerifiedDetails { get; private set; } = "";
    internal ElginPreferences(string name) {
        queue = name; var defaults = new PrinterDefaults { Access = 0xC }; // Administer + use this printer only.
        if (!OpenPrinter(name, out handle, ref defaults)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try {
            var info = Buffer((IntPtr p, uint size, out uint needed) => GetPrinterDriver(handle, null, 2, p, size, out needed));
            try {
                var driver = Marshal.PtrToStructure<DriverInfo2>(info);
                Driver = Marshal.PtrToStringUni(driver.Name) ?? "";
                if (Driver != "ELGIN i9(USB)") throw new IOException("Selecione uma fila que use exatamente ELGIN i9(USB), o modelo do pacote fornecido.");
                var hashes = new List<string>();
                foreach (var file in new[] { driver.DriverPath, driver.ConfigFile, driver.DataFile }) {
                    string path = Marshal.PtrToStringUni(file) ?? throw new IOException("Arquivo do driver não identificado.");
                    using var stream = File.OpenRead(path); hashes.Add(Convert.ToHexString(SHA256.HashData(stream)));
                }
                Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", hashes)))).ToLowerInvariant();
            } finally { Marshal.FreeHGlobal(info); }
        } catch { ClosePrinter(handle); throw; }
    }
    delegate bool FillBuffer(IntPtr buffer, uint size, out uint needed);
    static IntPtr Buffer(FillBuffer fill) {
        _ = fill(IntPtr.Zero, 0, out uint needed);
        if (needed == 0 || needed > 16777216) throw new Win32Exception(Marshal.GetLastWin32Error());
        var buffer = Marshal.AllocHGlobal(checked((int)needed));
        try { if (!fill(buffer, needed, out _)) throw new Win32Exception(Marshal.GetLastWin32Error()); return buffer; }
        catch { Marshal.FreeHGlobal(buffer); throw; }
    }
    static byte[] CopyMode(IntPtr mode) {
        int length = (ushort)Marshal.ReadInt16(mode, 68) + (ushort)Marshal.ReadInt16(mode, 70);
        if (length < 220 || length > 1048576) throw new IOException("DEVMODE retornado pelo driver tem tamanho inválido.");
        byte[] bytes = new byte[length]; Marshal.Copy(mode, bytes, 0, length); ReceiptProfile.ValidateMode(bytes); return bytes;
    }
    internal byte[]? Read(uint level) {
        var buffer = Buffer((IntPtr p, uint size, out uint needed) => GetPrinter(handle, level, p, size, out needed));
        try { var mode = Marshal.ReadIntPtr(buffer); return mode == IntPtr.Zero ? null : CopyMode(mode); }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    internal byte[]? Edit(IntPtr owner) {
        var current = Read(9) ?? Read(8) ?? throw new IOException("Fila não expôs preferências do driver.");
        return Normalize(current, owner, prompt: true);
    }
    internal byte[]? Normalize(byte[] mode, IntPtr owner = default, bool prompt = false) {
        ReceiptProfile.ValidateMode(mode);
        int size = DocumentProperties(owner, handle, queue, IntPtr.Zero, IntPtr.Zero, 0);
        if (size < mode.Length || size > 1048576) throw new IOException("Driver incompatível com o tamanho do perfil.");
        IntPtr input = Marshal.AllocHGlobal(mode.Length), output = Marshal.AllocHGlobal(size);
        try {
            Marshal.Copy(mode, 0, input, mode.Length);
            int result = DocumentProperties(owner, handle, queue, output, input, prompt ? 14u : 10u); // OUT | IN [+ IN_PROMPT]
            if (prompt && result == 2) return null;
            if (result != 1) throw new IOException("O driver não aceitou as preferências. Código=" + result);
            return CopyMode(output);
        } finally { Marshal.FreeHGlobal(input); Marshal.FreeHGlobal(output); }
    }
    void Set(uint level, byte[]? mode) {
        if (mode is not null) ReceiptProfile.ValidateMode(mode);
        IntPtr data = mode is null ? IntPtr.Zero : Marshal.AllocHGlobal(mode.Length), info = Marshal.AllocHGlobal(IntPtr.Size);
        try {
            if (mode is not null) Marshal.Copy(mode, 0, data, mode.Length);
            Marshal.WriteIntPtr(info, data);
            if (!SetPrinter(handle, level, info, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        } finally { if (data != IntPtr.Zero) Marshal.FreeHGlobal(data); Marshal.FreeHGlobal(info); }
    }
    internal string Backup(string directory) {
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "elgin-completo-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(file, Reports.Json(new ReceiptQueueBackup(2, Environment.MachineName, queue, Driver, Fingerprint, Read(8) ?? throw new IOException("Preferência global ausente."), Read(9), DateTimeOffset.Now, ReadDriverForms())));
        return file;
    }
    internal ReceiptProfile AutomaticProfile() => ElginReceiptPreset.Build(Read(8) ?? throw new IOException("O driver não expôs preferências globais."), Driver, Fingerprint);
    internal string Apply(ReceiptProfile profile, string backupDirectory) {
        profile.Compatible(Driver, Fingerprint);
        string backup = Backup(backupDirectory);
        bool automatic=profile.Method=="AutomaticPreset";ushort paperId=0;
        try {
            ReceiptPreferenceTransaction.Apply(() => {
                var requested=ReceiptProfile.ForQueue(profile.DevMode,queue);
                EnsureReceiptForm(requested);
                if(automatic) {
                    var before=ReadDriverForms();var desiredForms=ElginUserForms.EnsureReceipt(before);
                    if(before is null || !before.AsSpan().SequenceEqual(desiredForms))WriteDriverForms(desiredForms);
                    paperId=ResolvePaper();requested=ElginReceiptPreset.NamedPaper(requested,paperId);
                }
                var normalized=Normalize(requested)!;
                if(automatic)ElginReceiptPreset.VerifyNamedPaper(normalized,Driver,Fingerprint,paperId);
                if(automatic ? !requested.AsSpan(220).SequenceEqual(normalized.AsSpan(220)) : !ReceiptProfile.SameSettings(requested,normalized))throw new IOException("O driver não preservou as preferências solicitadas.");
                return normalized;
            },Read,Set,()=>Restore(backup),()=> {
                if(!automatic)return;
                // Reopen, reread and ask the driver to interpret persisted preferences.
                using var fresh=new ElginPreferences(queue);
                var details=new List<string>();
                foreach(uint level in new uint[]{8,9}) {
                    var stored=fresh.Read(level) ?? throw new IOException("Preferências gravadas ausentes.");
                    ElginReceiptPreset.VerifyNamedPaper(stored,fresh.Driver,fresh.Fingerprint,paperId);
                    var effective=fresh.Normalize(stored)!;
                    ElginReceiptPreset.VerifyNamedPaper(effective,fresh.Driver,fresh.Fingerprint,paperId);
                    details.Add((level==8?"Global: ":"Conta "+Environment.UserDomainName+"\\"+Environment.UserName+": ")+ElginReceiptPreset.Describe(effective));
                }
                if(fresh.ResolvePaper()!=paperId)throw new IOException("O cadastro de papel não persistiu ao reabrir a fila.");
                VerifiedDetails=string.Join("\r\n",details);
            });
        } catch(Exception e) {throw new IOException(e.Message+" Backup="+backup,e);}
        return backup;
    }
    byte[]? ReadDriverForms() {
        uint result=GetPrinterData(handle,"UserForm",out uint type,IntPtr.Zero,0,out uint needed);
        if(result==2)return null;
        if(result is not 0 and not 234 || needed>1048576 || type!=3)throw new IOException("GetPrinterData(UserForm): código="+result+", tipo="+type+". Lista preservada.");
        if(needed==0)return [];
        IntPtr buffer=Marshal.AllocHGlobal((int)needed);
        try {
            result=GetPrinterData(handle,"UserForm",out type,buffer,needed,out uint actual);
            if(result!=0 || type!=3 || actual!=needed)throw new IOException("A lista de papéis Elgin mudou durante a leitura. Código="+result);
            byte[] bytes=new byte[needed];Marshal.Copy(buffer,bytes,0,bytes.Length);return bytes;
        } finally {Marshal.FreeHGlobal(buffer);}
    }
    void WriteDriverForms(byte[]? data) {
        uint result=data is null ? DeletePrinterData(handle,"UserForm") : SetPrinterData(handle,"UserForm",3,data,(uint)data.Length);
        if(result!=0 && !(data is null && result==2))throw new Win32Exception((int)result,"Não foi possível gravar/restaurar os papéis do driver Elgin. Código="+result);
        var actual=ReadDriverForms();
        if(data is null ? actual is not null : actual is null || !actual.AsSpan().SequenceEqual(data))throw new IOException("A gravação dos papéis Elgin não foi confirmada.");
    }
    ushort ResolvePaper() {
        const int limit=4096;var blocks=new List<IntPtr>();
        string port;
        var info=Buffer((IntPtr p,uint size,out uint needed)=>GetPrinter(handle,2,p,size,out needed));
        try {port=Marshal.PtrToStringUni(Marshal.ReadIntPtr(info,3*IntPtr.Size)) ?? throw new IOException("Porta da fila não identificada.");}
        finally {Marshal.FreeHGlobal(info);}
        try {
            IntPtr Query(ushort capability,int stride,out int count) {
                count=DeviceCapabilities(queue,port,capability,IntPtr.Zero,IntPtr.Zero);
                if(count<=0 || count>limit)throw new IOException("O driver não listou os papéis. Capability="+capability+", retorno="+count);
                var buffer=Marshal.AllocHGlobal(limit*stride);blocks.Add(buffer);
                if(DeviceCapabilities(queue,port,capability,buffer,IntPtr.Zero)!=count)throw new IOException("A lista de papéis mudou durante a consulta.");return buffer;
            }
            var ids=Query(2,2,out int count);var names=Query(16,128,out int namesCount);var sizes=Query(3,8,out int sizesCount);
            if(count!=namesCount || count!=sizesCount)throw new IOException("Listas de papéis inconsistentes no driver.");
            var entries=new (ushort Id,string Name,int Width,int Height)[count];
            for(int i=0;i<count;i++)entries[i]=((ushort)Marshal.ReadInt16(ids,i*2),Marshal.PtrToStringUni(IntPtr.Add(names,i*128),64)!.Split('\0')[0],Marshal.ReadInt32(sizes,i*8),Marshal.ReadInt32(sizes,i*8+4));
            return ElginUserForms.ResolveReceipt(entries);
        } finally {foreach(var buffer in blocks)Marshal.FreeHGlobal(buffer);}
    }
    void EnsureReceiptForm(byte[] mode) {
        string formName = Encoding.Unicode.GetString(mode, 102, 64).TrimEnd('\0');
        if (!formName.Equals("Comprovantes", StringComparison.OrdinalIgnoreCase)) return;
        ReceiptPaperForm.Ensure(() => {
            var buffer = Buffer((IntPtr p, uint size, out uint needed) => GetForm(handle, formName, 1, p, size, out needed));
            try { var form = Marshal.PtrToStructure<FormInfo1>(buffer); return (form.Width, form.Height); }
            finally { Marshal.FreeHGlobal(buffer); }
        }, () => {
            var name = Marshal.StringToHGlobalUni(formName);
            try {
                var form = new FormInfo1 { Name = name, Width = 72000, Height = 100000, Right = 72000, Bottom = 100000 };
                if (!AddForm(handle, 1, ref form)) {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, "AddFormW: não foi possível criar automaticamente o papel Comprovantes 72×100 mm. Win32=" + error);
                }
            } finally { Marshal.FreeHGlobal(name); }
        });
    }
    internal void Restore(string file) {
        var saved = JsonSerializer.Deserialize<ReceiptQueueBackup>(File.ReadAllText(file)) ?? throw new IOException("Backup inválido.");
        if (saved.Format is not 1 and not 2 || saved.Computer != Environment.MachineName || saved.Queue != queue || saved.Driver != Driver || saved.Fingerprint != Fingerprint) throw new IOException("Backup de outra fila/PC/driver. Nenhuma preferência foi restaurada.");
        ReceiptProfile.ValidateMode(saved.Global); if (saved.User is not null) ReceiptProfile.ValidateMode(saved.User);
        if(saved.Format==2 && saved.DriverForms is not null && (saved.DriverForms.Length%ElginUserForms.RecordSize!=0 || saved.DriverForms.Length>1048576))throw new IOException("Backup contém lista de papéis Elgin inválida.");
        if(saved.Format==2)WriteDriverForms(saved.DriverForms);
        Set(8, saved.Global); Set(9, saved.User);
        if (!ReceiptProfile.SameSettings(saved.Global, Read(8) ?? [])) throw new IOException("Restauração global não foi confirmada.");
        var user = Read(9);
        if (saved.User is not null && !ReceiptProfile.SameSettings(saved.User, user ?? [])) throw new IOException("Restauração do perfil atual não foi confirmada.");
    }
    public void Dispose() => ClosePrinter(handle);
    [DllImport("winspool.drv",EntryPoint="GetPrinterDataW",CharSet=CharSet.Unicode)] static extern uint GetPrinterData(IntPtr printer,string name,out uint type,IntPtr data,uint size,out uint needed);
    [DllImport("winspool.drv",EntryPoint="SetPrinterDataW",CharSet=CharSet.Unicode)] static extern uint SetPrinterData(IntPtr printer,string name,uint type,byte[] data,uint size);
    [DllImport("winspool.drv",EntryPoint="DeletePrinterDataW",CharSet=CharSet.Unicode)] static extern uint DeletePrinterData(IntPtr printer,string name);
    [DllImport("winspool.drv",EntryPoint="DeviceCapabilitiesW",CharSet=CharSet.Unicode)] static extern int DeviceCapabilities(string device,string? port,ushort capability,IntPtr output,IntPtr mode);
    [StructLayout(LayoutKind.Sequential)] struct FormInfo1 { public uint Flags; public IntPtr Name; public int Width, Height, Left, Top, Right, Bottom; }
    [DllImport("winspool.drv", EntryPoint="GetFormW", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool GetForm(IntPtr handle, string name, uint level, IntPtr buffer, uint size, out uint needed);
    [DllImport("winspool.drv", EntryPoint="AddFormW", SetLastError=true)] static extern bool AddForm(IntPtr handle, uint level, ref FormInfo1 form);
    [StructLayout(LayoutKind.Sequential)] struct PrinterDefaults { public IntPtr Datatype, Devmode; public uint Access; }
    [StructLayout(LayoutKind.Sequential)] struct DriverInfo2 { public uint Version; public IntPtr Name, Environment, DriverPath, DataFile, ConfigFile; }
    [DllImport("winspool.drv", EntryPoint="OpenPrinterW", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool OpenPrinter(string name, out IntPtr handle, ref PrinterDefaults defaults);
    [DllImport("winspool.drv", SetLastError=true)] static extern bool ClosePrinter(IntPtr handle);
    [DllImport("winspool.drv", EntryPoint="GetPrinterW", SetLastError=true)] static extern bool GetPrinter(IntPtr handle, uint level, IntPtr buffer, uint size, out uint needed);
    [DllImport("winspool.drv", EntryPoint="SetPrinterW", SetLastError=true)] static extern bool SetPrinter(IntPtr handle, uint level, IntPtr buffer, uint command);
    [DllImport("winspool.drv", EntryPoint="GetPrinterDriverW", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool GetPrinterDriver(IntPtr handle, string? environment, uint level, IntPtr buffer, uint size, out uint needed);
    [DllImport("winspool.drv", EntryPoint="DocumentPropertiesW", CharSet=CharSet.Unicode, SetLastError=true)] static extern int DocumentProperties(IntPtr owner, IntPtr handle, string name, IntPtr output, IntPtr input, uint mode);
}
