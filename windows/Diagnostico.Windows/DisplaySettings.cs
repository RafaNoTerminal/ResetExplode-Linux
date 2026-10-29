using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace Diagnostico.Windows;

internal sealed record DisplayMode(string Device, byte[] Bytes)
{
    internal int Width => Read(172); internal int Height => Read(176); internal int Hz => Read(184);
    internal int Read(int offset) => BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(offset, 4));
    public override string ToString() => Device + $" · {Width} × {Height} · {Hz} Hz";
}
internal record DisplayBackup(string Computer, DateTimeOffset Date, string Device, byte[] Before);
internal static class DisplaySettings
{
    internal static byte[] Empty() { var b = new byte[220]; BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(68, 2), 220); return b; }
    internal static DisplayMode Current(string device) { var b = Empty(); if (!EnumDisplaySettings(device, -1, b)) throw new IOException("Não foi possível ler o modo atual de " + device); return new(device, b); }
    internal static List<DisplayMode> Modes(string device)
    {
        var result = new List<DisplayMode>();
        for (int i = 0; i < 10000; i++) { var b = Empty(); if (!EnumDisplaySettings(device, i, b)) break; result.Add(new(device, b)); }
        return result;
    }
    internal static DisplayMode Stable60(string device)
    {
        var before = Current(device);
        return Modes(device).Where(m => m.Width == before.Width && m.Height == before.Height && m.Read(168) == before.Read(168) && m.Read(180) == before.Read(180) && m.Read(84) == before.Read(84) && m.Hz is 59 or 60)
            .OrderByDescending(m => m.Hz).FirstOrDefault() ?? throw new IOException("Esta tela não anuncia modo de 59/60 Hz na resolução e orientação atuais. Nenhuma mudança foi feita.");
    }
    internal static void Apply(DisplayMode mode, uint flags)
    {
        if (mode.Bytes.Length != 220) throw new IOException("Modo de vídeo inválido.");
        // Preserve position, orientation, resolution and format; only change the refresh rate.
        byte[] b = (byte[])Current(mode.Device).Bytes.Clone();
        mode.Bytes.AsSpan(184, 4).CopyTo(b.AsSpan(184, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(72, 4), 0x00400000);
        int result = ChangeDisplaySettingsEx(mode.Device, b, IntPtr.Zero, flags, IntPtr.Zero);
        if (result != 0) throw new IOException("Windows não aplicou o modo. Código ChangeDisplaySettingsEx=" + result + ".");
    }
    internal static string Save(DisplayMode before)
    {
        Directory.CreateDirectory(Automation.BackupDirectory);
        string file = Path.Combine(Automation.BackupDirectory, "tela-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6] + ".json");
        File.WriteAllText(file, JsonSerializer.Serialize(new DisplayBackup(Environment.MachineName, DateTimeOffset.Now, before.Device, before.Bytes))); return file;
    }
    internal static DisplayMode Load(string file)
    {
        var data = JsonSerializer.Deserialize<DisplayBackup>(File.ReadAllText(file)) ?? throw new IOException("Backup de tela inválido.");
        if (data.Computer != Environment.MachineName || data.Before.Length != 220 || !Screen.AllScreens.Any(s => s.DeviceName == data.Device)) throw new IOException("Backup pertence a outro PC ou tela desconectada.");
        var mode = new DisplayMode(data.Device, data.Before); var now = Current(data.Device);
        if (mode.Width != now.Width || mode.Height != now.Height || mode.Read(84) != now.Read(84)) throw new IOException("Resolução ou orientação mudou desde o backup. Use as configurações de tela para escolher um modo compatível.");
        if (!Modes(data.Device).Any(m => m.Hz == mode.Hz && m.Width == now.Width && m.Height == now.Height && m.Read(168) == now.Read(168))) throw new IOException("O modo do backup não está mais disponível.");
        return mode;
    }
    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool EnumDisplaySettings(string device, int mode, [In, Out] byte[] data);
    [DllImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExW", CharSet = CharSet.Unicode)]
    static extern int ChangeDisplaySettingsEx(string device, [In] byte[] data, IntPtr hwnd, uint flags, IntPtr param);
}

public sealed partial class MainForm
{
    void ConfigureDisplay(bool restore = false)
    {
        if (task is not null) return;
        try {
            DisplayMode target;
            if (restore) {
                using var choose = new OpenFileDialog { InitialDirectory = Automation.BackupDirectory, Filter = "Backup de tela (tela-*.json)|tela-*.json" };
                if (choose.ShowDialog(this) != DialogResult.OK) return; target = DisplaySettings.Load(choose.FileName);
            } else {
                using var choose = new Form { Text = "Escolher tela para testar 60 Hz", Size = new Size(650, 260), StartPosition = FormStartPosition.CenterParent };
                var list = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
                foreach (var screen in Screen.AllScreens) list.Items.Add(DisplaySettings.Current(screen.DeviceName));
                if (list.Items.Count == 0) throw new IOException("Nenhuma tela encontrada."); list.SelectedIndex = 0;
                var apply = new Button { Text = "Testar 60 Hz mantendo resolução", Dock = DockStyle.Bottom, Height = 45, DialogResult = DialogResult.OK };
                choose.Controls.Add(list); choose.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Teste para cintilação/troca de frequência. Não corrige cabo, defeito físico ou driver. A imagem volta ao modo anterior em 15 segundos se você não confirmar.", Padding = new Padding(10, 35, 10, 0) }); choose.Controls.Add(apply);
                if (choose.ShowDialog(this) != DialogResult.OK) return;
                target = DisplaySettings.Stable60(((DisplayMode)list.SelectedItem!).Device);
            }
            var before = DisplaySettings.Current(target.Device);
            if (before.Hz == target.Hz) { MessageBox.Show(this, "A tela já está em " + target.Hz + " Hz. Nenhuma alteração necessária.", Brand.Name); return; }
            DisplaySettings.Apply(target, 2); // CDS_TEST; no changes.
            string backup = DisplaySettings.Save(before); bool kept = false, changed = false;
            try {
                DisplaySettings.Apply(target, 0); changed = true;
                using var confirm = new Form { Text = "Confirmar imagem", Size = new Size(520, 230), StartPosition = FormStartPosition.CenterScreen, TopMost = true };
                int seconds = 15; var label = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
                void Update() => label.Text = "A imagem ficou boa em " + target.Hz + " Hz?\nReverte em " + seconds + " segundos.";
                var keep = new Button { Text = "Manter esta configuração", Dock = DockStyle.Bottom, Height = 45, DialogResult = DialogResult.OK };
                confirm.Controls.Add(label); confirm.Controls.Add(keep); Update();
                using var timer = new System.Windows.Forms.Timer { Interval = 1000 };
                timer.Tick += (_, _) => { seconds--; Update(); if (seconds <= 0) { timer.Stop(); confirm.DialogResult = DialogResult.Cancel; confirm.Close(); } };
                timer.Start();
                if (confirm.ShowDialog(this) == DialogResult.OK) {
                    DisplaySettings.Apply(target, 1); // Persist only after visual confirmation.
                    if (DisplaySettings.Current(target.Device).Hz != target.Hz) throw new IOException("O Windows não confirmou a frequência solicitada.");
                    kept = true;
                }
            } finally { if (changed && !kept) { DisplaySettings.Apply(before, 1); if (DisplaySettings.Current(before.Device).Hz != before.Hz) throw new IOException("Reversão não confirmada. Abra Configurar telas e restaure o modo anterior. Backup: " + backup); } }
            var result = TestResult.Done(restore ? "Restaurar tela" : "Ajustar tela", ("Resultado", kept ? "Aplicado e confirmado visualmente" : "Revertido; configuração anterior restaurada"), ("Tela", target.Device), ("Antes", before.Hz), ("Solicitado", target.Hz), ("Backup", backup));
            results.Add(result); status.Text = kept ? "Tela ajustada e conferida." : "Teste de tela revertido.";
        } catch (Exception e) { results.Add(new("Ajustar tela", "Não concluído", new() { ["Erro"] = e.Message }, DateTimeOffset.Now)); MessageBox.Show(this, e.Message, Brand.Name); }
    }
}
