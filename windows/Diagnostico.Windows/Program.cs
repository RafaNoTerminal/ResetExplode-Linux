namespace Diagnostico.Windows;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length >= 1 && args[0] == "--smoke-test") {
            try {
                using var form = new MainForm(collectOnShow: false); form.Show(); Application.DoEvents();
                var inventory = Inventory.Collect();
                form.VerifyUi(inventory);
                form.VerifyTaskAndFailureUi(args.Length >= 2 ? args[1] : null);
                SmokeChecks.VerifyAdministratorManifest();
                SmokeChecks.VerifyPackageFailures();
                SmokeChecks.VerifyNativeExitCode();
                MaintenanceSmoke.Verify();
                CompanySmoke.Verify();
                if (!inventory.Any(x => x.Grupo == "CPU" && x.Campo == "Modelo")) throw new Exception("CPU ausente no inventário.");
                if (Inventory.Memory().Total <= 0 || Inventory.Processes().Count == 0) throw new Exception("Coleta Windows falhou.");
                Automation.VerifyRegistryCodec();
                // Parse the exact embedded PowerShell, but never invoke package removal in CI.
                var actions = AutomationCatalog.Select(["app:Microsoft.GetHelp"]);
                var packagePreview = Task.Run(() => Automation.Packages(actions, apply: false, CancellationToken.None)).GetAwaiter().GetResult();
                using (var preview = System.Text.Json.JsonDocument.Parse(packagePreview)) { if(preview.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) throw new Exception("Prévia Appx não retornou lista."); }
                if(Vivobook.Collect().Count == 0) throw new Exception("Coleta ASUS ausente.");
                _ = OneDriveUninstall.Plan(OneDriveDetection.Read(), File.Exists); // Detection only; never execute an installed uninstaller in CI.
                var script = AutomationCatalog.PackageScript(actions, apply: true);
                string parse = "$tokens=$null;$errors=$null;[System.Management.Automation.Language.Parser]::ParseInput([System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(script)) + "')),[ref]$tokens,[ref]$errors)|Out-Null;if($errors.Count){throw ($errors|Out-String)}";
                using (var ps = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, Arguments = "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(parse)) })) { ps!.WaitForExit(); if(ps.ExitCode != 0) throw new Exception("Sintaxe PowerShell inválida."); }
                if (args.Length >= 2) {
                    using var image = new Bitmap(form.Width, form.Height); form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(args[1]);
                }
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-result.txt"), "OK · 16 páginas, perfil empresa/filas simuladas/temporários controlados/pingTCP local/sensores sem driver no CI, detecção PC/notebook, tela somente leitura/CDS_TEST, Windows Update simulado, ativação somente fixture/hash/consulta (motor real não executado), manifesto/admin, botão somente em testes, resultado por item/falhas, Appx protegido preservado, WMI/RAM/processos/ASUS, backup em chave isolada e sintaxe PowerShell; nenhuma remoção ou ajuste real aplicado.");
                form.Close(); return 0;
            } catch (Exception e) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-result.txt"), e.ToString()); return 1; }
        }
        if (!Automation.Admin) { MessageBox.Show("Abra o aplicativo confirmando o acesso de administrador solicitado pelo Windows.", Brand.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning); return 1; }
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, Brand.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new MainForm()); return 0;
    }
}
