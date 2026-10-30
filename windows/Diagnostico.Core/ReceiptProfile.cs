using System.Buffers.Binary;
using System.Text;
namespace Diagnostico;

// Stores the driver's complete Unicode DEVMODE, including its proprietary settings.
// Photos/PrintTicket alone cannot reconstruct those private fields.
public record ReceiptProfile(int Format, string Driver, string DriverFingerprint, byte[] DevMode, bool ConfirmedByTechnician, DateTimeOffset CapturedAt)
{
    public void Validate() {
        if (Format != 1 || !ConfirmedByTechnician || Driver != "ELGIN i9(USB)" || DriverFingerprint.Length != 64 || !DriverFingerprint.All(Uri.IsHexDigit)) throw new ArgumentException("Perfil Elgin inválido ou não conferido pelo técnico.");
        ValidateMode(DevMode);
        if (DevMode.Length == BinaryPrimitives.ReadUInt16LittleEndian(DevMode.AsSpan(68))) throw new ArgumentException("Perfil não contém preferências privadas do driver Elgin.");
    }
    public void Compatible(string driver, string fingerprint) {
        Validate();
        if (driver != Driver || !string.Equals(fingerprint, DriverFingerprint, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Driver diferente do perfil (nome ou arquivos/versão). Configure e capture outro perfil com o driver instalado neste PC.");
    }
    public static void ValidateMode(byte[] mode) {
        if (mode is null || mode.Length < 220 || mode.Length > 1048576) throw new ArgumentException("DEVMODE inválido.");
        int header = BinaryPrimitives.ReadUInt16LittleEndian(mode.AsSpan(68)), extra = BinaryPrimitives.ReadUInt16LittleEndian(mode.AsSpan(70));
        if (header < 220 || header + extra != mode.Length) throw new ArgumentException("Tamanho do DEVMODE não corresponde aos dados do driver.");
    }
    public static byte[] ForQueue(byte[] mode, string name) {
        ValidateMode(mode);
        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl)) throw new ArgumentException("Fila inválida.");
        byte[] copy = (byte[])mode.Clone(); Array.Clear(copy, 0, 64);
        Encoding.Unicode.GetBytes(name[..Math.Min(name.Length, 31)]).CopyTo(copy, 0); return copy;
    }
    public static bool SameSettings(byte[] left, byte[] right) {
        ValidateMode(left); ValidateMode(right);
        // dmDeviceName changes when a profile is transferred to another named queue.
        return left.AsSpan(64).SequenceEqual(right.AsSpan(64));
    }
}

public static class ReceiptPreferenceTransaction
{
    public static void Apply(byte[] desired, Func<uint, byte[]?> read, Action<uint, byte[]?> write, Action restore) {
        ReceiptProfile.ValidateMode(desired);
        try {
            write(8, desired); write(9, desired);
            if (!ReceiptProfile.SameSettings(desired, read(8) ?? []) || !ReceiptProfile.SameSettings(desired, read(9) ?? [])) throw new IOException("Releitura não confirmou as preferências completas.");
        } catch (Exception applyError) {
            try { restore(); } catch (Exception rollbackError) { throw new IOException("Falha ao aplicar e ao restaurar: " + applyError.Message + "; restauração=" + rollbackError.Message); }
            throw new IOException("Preferências anteriores restauradas após falha: " + applyError.Message);
        }
    }
}
