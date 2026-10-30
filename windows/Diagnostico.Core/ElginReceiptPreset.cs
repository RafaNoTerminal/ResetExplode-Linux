using System.Buffers.Binary;
using System.Text;
namespace Diagnostico;

// Codec for the exact supplied Elgin 1.8.0 x64 package, UI DLL 2.7.2.0.
// The offsets and enum values were checked by round-tripping the actual vendor
// Cut Paper / Logo property-page helpers on a memory fixture, with no printer.
public static class ElginReceiptPreset
{
    public const string Fingerprint = "f2e32302b6e6d220dcfd3e92b8e80c54f1f958438a93adbd4d3d1e0db9c5fe41";
    public const string DriverName = "ELGIN i9(USB)";
    public const int ModeLength = 4688; // DEVMODEW 220 + vendor extra 4468.
    public const string Description = "Papel Comprovantes 72 × 100 mm; retrato, preto e branco, frente única. Corte parcial no fim da página, distância 0 mm. Logotipo salvo na impressora no início da página, centralizado (Meio), código chave 0 / 0 (48 / 48).";
    const int CutWhen = 0x33c, CutMode = 0x340, CutDistance = 0x344, LogoEvents = 0x3a0;
    const int StartPageLogo = 0x5b8, LogoMode = StartPageLogo + 0x208, Key1 = StartPageLogo + 0x20c, Key2 = StartPageLogo + 0x20d, Position = StartPageLogo + 0x210;
    static int Read32(byte[] b, int offset) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(offset));
    static int Read16(byte[] b, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(offset));
    static void Put32(byte[] b, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(offset), value);
    static void Put16(byte[] b, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(offset), value);
    static void Compatible(byte[] mode, string driver, string fingerprint) {
        ReceiptProfile.ValidateMode(mode);
        if (driver != DriverName || !Fingerprint.Equals(fingerprint, StringComparison.OrdinalIgnoreCase) || mode.Length != ModeLength || Read16(mode, 68) != 220 || Read16(mode, 70) != 4468) throw new ArgumentException("Ajuste automático disponível para o driver Elgin 1.8.0 x64 fornecido no pendrive. Nome, arquivos e formato do driver precisam corresponder; instale o pacote correto antes de configurar.");
    }
    public static ReceiptProfile Build(byte[] current, string driver, string fingerprint) {
        Compatible(current, driver, fingerprint); var mode = (byte[])current.Clone();
        Put32(mode, 72, Read32(mode, 72) | 0x1180f); // FORMNAME, DUPLEX, COLOR, WIDTH, LENGTH, SIZE, ORIENTATION.
        Put16(mode, 76, 1); Put16(mode, 78, 256); Put16(mode, 80, 1000); Put16(mode, 82, 720);
        Put16(mode, 92, 1); Put16(mode, 94, 1);
        Array.Clear(mode, 102, 64); Encoding.Unicode.GetBytes("Comprovantes").CopyTo(mode, 102);
        Put32(mode, CutWhen, 8); Put32(mode, CutMode, 1); Put32(mode, CutDistance, 0);
        Put32(mode, LogoEvents, (Read32(mode, LogoEvents) & ~15) | 4);
        // Start-page record: no external file; stored-printer logo selected by ASCII keys.
        Array.Clear(mode, StartPageLogo, 0x214); Put32(mode, LogoMode, 2); mode[Key1] = 48; mode[Key2] = 48; Put32(mode, Position, 2);
        var result = new ReceiptProfile(1, driver, fingerprint, mode, false, DateTimeOffset.Now, "AutomaticPreset");
        Verify(mode, driver, fingerprint); return result;
    }
    public static void Verify(byte[] mode, string driver, string fingerprint) {
        Compatible(mode, driver, fingerprint);
        if (Read16(mode, 76) != 1 || Read16(mode, 80) != 1000 || Read16(mode, 82) != 720 || Read16(mode, 92) != 1 || Read16(mode, 94) != 1 || Encoding.Unicode.GetString(mode, 102, 64).TrimEnd('\0') != "Comprovantes" || (Read32(mode,72) & 0x1180d) != 0x1180d || Read32(mode, CutWhen) != 8 || Read32(mode, CutMode) != 1 || Read32(mode, CutDistance) != 0 || (Read32(mode, LogoEvents) & 15) != 4 || Read32(mode, LogoMode) != 2 || mode[Key1] != 48 || mode[Key2] != 48 || Read32(mode, Position) != 2) throw new IOException("O driver não confirmou todos os ajustes automáticos de papel, corte parcial e logotipo. Confira o resultado; a aplicação não foi aprovada.");
    }
}
