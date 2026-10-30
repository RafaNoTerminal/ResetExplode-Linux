using System.Text.Json;
namespace Diagnostico.Windows;
internal static class ElginBackupSmoke
{
    internal static void Verify() {
        byte[] mode=new byte[220];mode[68]=220;
        var current=new ReceiptQueueBackup(2,"FixturePC","FixtureQueue","FixtureDriver",new string('a',64),mode,null,DateTimeOffset.UtcNow,ElginUserForms.EnsureReceipt(null));
        var parsed=JsonSerializer.Deserialize<ReceiptQueueBackup>(Reports.Json(current))!;
        if(parsed.Format!=2 || !parsed.Global.SequenceEqual(mode) || parsed.DriverForms is null || !parsed.DriverForms.SequenceEqual(current.DriverForms!))throw new Exception("Backup2 perdeu preferências ou papéis do driver.");
        string legacy=JsonSerializer.Serialize(new{Format=1,current.Computer,current.Queue,current.Driver,current.Fingerprint,current.Global,current.User,current.SavedAt});
        parsed=JsonSerializer.Deserialize<ReceiptQueueBackup>(legacy)!;
        if(parsed.Format!=1 || parsed.DriverForms is not null || !parsed.Global.SequenceEqual(mode))throw new Exception("Backup1 antigo deixou de ser compatível.");
    }
}
