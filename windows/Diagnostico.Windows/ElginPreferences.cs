using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Diagnostico.Windows;

internal record ReceiptQueueBackup(int Format, string Computer, string Queue, string Driver, string Fingerprint, byte[] Global, byte[]? User, DateTimeOffset SavedAt);
internal sealed class ElginPreferences : IDisposable
{
    readonly IntPtr handle; readonly string queue;
    internal string Driver { get; }
    internal string Fingerprint { get; }
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
        File.WriteAllText(file, Reports.Json(new ReceiptQueueBackup(1, Environment.MachineName, queue, Driver, Fingerprint, Read(8) ?? throw new IOException("Preferência global ausente."), Read(9), DateTimeOffset.Now)));
        return file;
    }
    internal ReceiptProfile AutomaticProfile() => ElginReceiptPreset.Build(Read(8) ?? throw new IOException("O driver não expôs preferências globais."), Driver, Fingerprint);
    internal string Apply(ReceiptProfile profile, string backupDirectory) {
        profile.Compatible(Driver, Fingerprint);
        string backup = Backup(backupDirectory);
        var requested = ReceiptProfile.ForQueue(profile.DevMode, queue);
        EnsureReceiptForm(requested);
        var normalized = Normalize(requested)!;
        if (profile.Method == "AutomaticPreset") ElginReceiptPreset.Verify(normalized, Driver, Fingerprint);
        if (profile.Method == "AutomaticPreset" ? !requested.AsSpan(220).SequenceEqual(normalized.AsSpan(220)) : !ReceiptProfile.SameSettings(requested, normalized)) throw new IOException((profile.Method == "AutomaticPreset" ? "O driver não preservou os ajustes automáticos. Nenhuma preferência foi aplicada; confira se o pacote Elgin é o fornecido. Backup=" : "O driver normalizou o modelo para preferências diferentes. Nenhuma preferência foi aplicada. Configure uma fila modelo neste PC e capture novamente. Backup=") + backup);
        try { ReceiptPreferenceTransaction.Apply(normalized, Read, Set, () => Restore(backup)); }
        catch (Exception e) { throw new IOException(e.Message + " Backup=" + backup); }
        return backup;
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
        if (saved.Format != 1 || saved.Computer != Environment.MachineName || saved.Queue != queue || saved.Driver != Driver || saved.Fingerprint != Fingerprint) throw new IOException("Backup de outra fila/PC/driver. Nenhuma preferência foi restaurada.");
        ReceiptProfile.ValidateMode(saved.Global); if (saved.User is not null) ReceiptProfile.ValidateMode(saved.User);
        Set(8, saved.Global); Set(9, saved.User);
        if (!ReceiptProfile.SameSettings(saved.Global, Read(8) ?? [])) throw new IOException("Restauração global não foi confirmada.");
        var user = Read(9);
        if (saved.User is not null && !ReceiptProfile.SameSettings(saved.User, user ?? [])) throw new IOException("Restauração do perfil atual não foi confirmada.");
    }
    public void Dispose() => ClosePrinter(handle);
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
