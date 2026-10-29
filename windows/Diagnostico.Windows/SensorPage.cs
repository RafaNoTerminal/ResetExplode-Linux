namespace Diagnostico.Windows;

public record SensorRow(string Grupo, string Tipo, string Sensor, string Atual, string Minimo, string Maximo, string Unidade, string Origem);

public sealed partial class MainForm
{
    readonly SensorMonitor sensorMonitor = new();
    readonly System.Windows.Forms.Timer sensorTimer = new() { Interval = 2000 };
    List<SensorRow> sensorRows = []; DateTimeOffset? sensorAt; bool sensorBusy, sensorWired;
    DataGridView? sensorGrid; Label? sensorNotice; bool onlyTemperatures = true;
    void RenderSensorRows() {
        if (sensorGrid is not { IsDisposed: false }) return;
        sensorGrid.DataSource = sensorRows.Where(r => !onlyTemperatures || r.Tipo == "Temperature").OrderBy(r => r.Grupo).ThenBy(r => r.Tipo == "Temperature" ? 0 : 1).ThenBy(r => r.Sensor).ToList();
    }
    async Task RefreshSensors(bool start = false)
    {
        if (sensorBusy) return; sensorBusy = true;
        try {
            sensorRows = await Task.Run(() => sensorMonitor.Read(start)); sensorAt = DateTimeOffset.Now;
            RenderSensorRows();
            if (sensorNotice is { IsDisposed: false }) sensorNotice.Text = sensorMonitor.Notice + "\nAtualizado: " + sensorAt.Value.ToLocalTime().ToString("T") + " · mínimos/máximos desde o início da leitura.";
        } catch (Exception e) { if (sensorNotice is { IsDisposed: false }) sensorNotice.Text = "Leitura não disponível: " + e.Message; sensorTimer.Stop(); if (start) throw; }
        finally { sensorBusy = false; }
    }
    void BuildSensors()
    {
        sensorGrid = Grid(); RenderSensorRows(); sensorNotice = new Label { Dock = DockStyle.Top, Height = 95, ForeColor = Color.Silver, Text = sensorMonitor.Notice }; content.Controls.Add(sensorNotice);
        if (!sensorWired) { sensorTimer.Tick += async (_, _) => { if (sensorMonitor.Active) await RefreshSensors(); }; sensorWired = true; }
        var filter = Actions();
        var temperatures = new CheckBox { Text = "Temperaturas (°C) · CPU, GPU, placa-mãe e discos", Checked = onlyTemperatures, AutoSize = true, ForeColor = Ink, Margin = new Padding(0, 8, 10, 0) };
        temperatures.CheckedChanged += (_, _) => { onlyTemperatures = temperatures.Checked; RenderSensorRows(); }; filter.Controls.Add(temperatures);
        var controls = Actions();
        Button(controls, "Ler / recarregar sensores", () => _ = Run("Sensores", async ct => { sensorTimer.Stop(); sensorMonitor.Dispose(); await RefreshSensors(start: true); if (sensorMonitor.Active) sensorTimer.Start(); return TestResult.Done("Sensores", ("Iniciado", sensorMonitor.Active), ("Observação", sensorMonitor.Notice)); }), true);
        Button(controls, "Parar leitura", () => { sensorTimer.Stop(); sensorMonitor.Dispose(); if (!sensorNotice.IsDisposed) sensorNotice.Text = sensorMonitor.Notice; });
        Button(controls, "Salvar amostra no relatório", () => { results.Add(TestResult.Done("Sensores · amostra", ("Horário", sensorAt), ("Sensores", sensorRows.ToArray()), ("Observação", sensorMonitor.Notice))); status.Text = "Amostra incluída em Resultados; exporte HTML + JSON para guardar."; });
        var support = Actions();
        Button(support, "Instalar suporte de sensores", () => {
            if (task is not null) return;
            if (MessageBox.Show(this, "Instalar o driver opcional PawnIO2.2.0 para leitura de sensores CPU/placa-mãe?\n\nDownload do projeto original, SHA256 fixo e assinatura digital conferidos. Requer administrador e pode pedir reinício. O monitor será recarregado depois de instalar. Não é necessário para todos os sensores GPU/disco.\n\nO RBT não enfraquece políticas de drivers/segurança para instalar.", Brand.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            sensorTimer.Stop(); sensorMonitor.Dispose(); _ = Run("Instalar suporte de sensores", async ct => {
                var result = await SensorSupport.Install(ct);
                if (result.Dados.TryGetValue("Retorno", out var code) && code is int exit && exit is 0 or 3010) {
                    await RefreshSensors(start: true); if (sensorMonitor.Active) sensorTimer.Start();
                    result.Dados["Leitura após instalação"] = sensorMonitor.Notice;
                }
                return result;
            });
        }, true);
        Button(support, "Suporte PawnIO", () => OpenLink("https://github.com/namazso/PawnIO"));
        Heading("Sensores de hardware", "Temperaturas, RPM, carga, clocks, consumo, tensões e sensores de discos/bateria disponíveis. Leitura a cada 2 s; mínimos/máximos da sessão. Não altera rotação ou parâmetros do hardware. ACPI não é usado como temperatura da CPU.");
    }
}
