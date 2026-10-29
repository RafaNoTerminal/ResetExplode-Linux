using System.Runtime.InteropServices;

namespace Diagnostico.Windows;

public sealed class DisplayTest : Form
{
    readonly Color[] colors = [Color.Red, Color.Lime, Color.Blue, Color.White, Color.Black, Color.Gray]; int current;
    public DisplayTest() {
        Text = "Teste de tela · Esc sai"; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
        Bounds = Screen.FromPoint(Cursor.Position).Bounds; BackColor = colors[0]; KeyPreview = true; TopMost = true; Cursor = Cursors.Cross;
        var help = new Label { Text = "Clique / Espaço: próxima cor   ·   Esc: sair", AutoSize = true, ForeColor = Color.White, BackColor = Color.FromArgb(35, 35, 35), Padding = new Padding(10), Location = new Point(20, 20) }; Controls.Add(help);
        void Next() { current = (current + 1) % colors.Length; BackColor = colors[current]; }
        Click += (_, _) => Next(); help.Click += (_, _) => Next(); KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); else if (e.KeyCode is Keys.Space or Keys.Enter or Keys.Right) Next(); };
    }
}
public sealed class InputTest : Form
{
    public InputTest() {
        Text = "Teste de teclado e mouse"; Size = new Size(700, 450); StartPosition = FormStartPosition.CenterParent; KeyPreview = true; BackColor = Color.FromArgb(30, 34, 41); ForeColor = Color.White;
        var log = new Label { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 18), TextAlign = ContentAlignment.MiddleCenter, Text = "Clique e pressione as teclas aqui.\nEsc fecha esta janela." }; Controls.Add(log);
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); else log.Text = "Tecla recebida: " + e.KeyData; e.SuppressKeyPress = true; };
        log.MouseDown += (_, e) => log.Text = "Mouse: " + e.Button + "\nPosição: " + e.X + ", " + e.Y;
        log.MouseWheel += (_, e) => log.Text = "Rolagem: " + e.Delta; log.MouseEnter += (_, _) => Focus();
    }
}
public static class AudioTest
{
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool PlaySound(string? file, IntPtr module, uint flags);
    public static async Task Play(CancellationToken ct) {
        string file = Path.Combine(Path.GetTempPath(), "rbt-audio-" + Guid.NewGuid().ToString("N") + ".wav");
        try {
            const int rate = 44100, seconds = 6, length = rate * seconds * 4;
            using (var w = new BinaryWriter(File.Open(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))) {
                w.Write("RIFF"u8); w.Write(36 + length); w.Write("WAVEfmt "u8); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16); w.Write("data"u8); w.Write(length);
                for (int i = 0; i < rate * seconds; i++) { int phase = i / (rate * 2); double t = (i % (rate * 2)) / (double)rate; short value = t < 1.5 ? (short)(Math.Sin(2 * Math.PI * 440 * t) * 7000 * Math.Min(1, t * 20) * Math.Min(1, (1.5 - t) * 20)) : (short)0; w.Write(phase == 1 ? (short)0 : value); w.Write(phase == 0 ? (short)0 : value); }
            }
            if (!PlaySound(file, IntPtr.Zero, 0x00020000 | 0x0001 | 0x0002)) throw new IOException("Windows não iniciou a reprodução de áudio.");
            await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
        } finally { PlaySound(null, IntPtr.Zero, 0); if (File.Exists(file)) File.Delete(file); }
    }
}
