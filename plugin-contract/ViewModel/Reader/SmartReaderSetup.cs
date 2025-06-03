#region copyright
//****************************************************************************************************
// Copyright ©2025 Impinj, Inc.All rights reserved.              
//                                    
// You may use and modify this code under the terms of the Impinj Software Tools License & Disclaimer. 
// Visit https://support.impinj.com/hc/en-us/articles/360000468370-Software-Tools-License-Disclaimer   
// for full license details, or contact Impinj, Inc.at support@impinj.com for a copy of the license.   
//
//****************************************************************************************************
#endregion
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Globalization;

namespace SmartReaderJobs.ViewModel.Reader;

public partial class SmartReaderSetup
{
    [JsonProperty("status", NullValueHandling = NullValueHandling.Ignore)]
    public string? Status { get; set; }

    [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
    public List<SmartReaderSetupData>? Data { get; set; }
}

public class SmartReaderSetupData
{
    [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? Id { get; set; }

    [JsonProperty("descricao", NullValueHandling = NullValueHandling.Ignore)]
    public string? Descricao { get; set; }

    [JsonProperty("serial", NullValueHandling = NullValueHandling.Ignore)]
    public string? Serial { get; set; }

    [JsonProperty("ip", NullValueHandling = NullValueHandling.Ignore)]
    public string? Ip { get; set; }

    [JsonProperty("porta", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? Porta { get; set; }

    [JsonProperty("tipo", NullValueHandling = NullValueHandling.Ignore)]
    public string? Tipo { get; set; }

    [JsonProperty("tipo_conexao", NullValueHandling = NullValueHandling.Ignore)]
    public string? TipoConexao { get; set; }

    [JsonProperty("local", NullValueHandling = NullValueHandling.Ignore)]
    public string? Local { get; set; }

    [JsonProperty("habilitar_mascara_epc", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? HabilitarMascaraEpc { get; set; }

    [JsonProperty("endereco_mascara_epc", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? EnderecoMascaraEpc { get; set; }

    [JsonProperty("tamanho_mascara_epc", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? TamanhoMascaraEpc { get; set; }

    [JsonProperty("habilitar_controle_leitor", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? HabilitarControleLeitor { get; set; }

    [JsonProperty("status", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? Status { get; set; }

    [JsonProperty("modo_leitor", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? ModoLeitor { get; set; }

    [JsonProperty("modo_busca", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? ModoBusca { get; set; }

    [JsonProperty("sessao", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? Sessao { get; set; }

    [JsonProperty("fastid", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? Fastid { get; set; }

    [JsonProperty("populacao_estimada", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? PopulacaoEstimada { get; set; }

    [JsonProperty("ultima_comunicacao", NullValueHandling = NullValueHandling.Ignore)]
    public DateTimeOffset UltimaComunicacao { get; set; }

    [JsonProperty("ativar_gpo1", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? AtivarGpo1 { get; set; }

    [JsonProperty("modo_gpo1", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? ModoGpo1 { get; set; }

    [JsonProperty("ativar_gpo2", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? AtivarGpo2 { get; set; }

    [JsonProperty("modo_gpo2", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? ModoGpo2 { get; set; }

    [JsonProperty("ativar_gpo3", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? AtivarGpo3 { get; set; }

    [JsonProperty("modo_gpo3", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? ModoGpo3 { get; set; }

    [JsonProperty("ativar_gpo4", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? AtivarGpo4 { get; set; }

    [JsonProperty("modo_gpo4", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? ModoGpo4 { get; set; }

    [JsonConverter(typeof(ParseStringConverter))]
    [JsonProperty("ativar_gpo_externo", NullValueHandling = NullValueHandling.Ignore)]
    public long? AtivarGpoExterno { get; set; }

    [JsonProperty("endereco_gpo_externo")] public string? EnderecoGpoExterno { get; set; }

    [JsonProperty("porta_gpo_externo", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? PortaGpoExterno { get; set; }

    [JsonProperty("comando_gpo_externo", NullValueHandling = NullValueHandling.Ignore)]
    public string? ComandoGpoExterno { get; set; }

    [JsonProperty("usar_conexao_segura", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? UsarConexaoSegura { get; set; }

    [JsonProperty("usuario", NullValueHandling = NullValueHandling.Ignore)]
    public string? Usuario { get; set; }

    [JsonProperty("senha", NullValueHandling = NullValueHandling.Ignore)]
    public string? Senha { get; set; }

    [JsonProperty("filtro_epc_software", NullValueHandling = NullValueHandling.Ignore)]
    public string? FiltroEpcSoftware { get; set; }

    [JsonProperty("habilitar_tarefas", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? HabilitarTarefas { get; set; }
}

public partial class SmartReaderSetup
{
    public static SmartReaderSetup FromJson(string json)
    {
        return JsonConvert.DeserializeObject<SmartReaderSetup>(json, Converter.Settings);
    }
}

public static class Serialize
{
    public static string ToJson(this SmartReaderSetup self)
    {
        return JsonConvert.SerializeObject(self, Converter.Settings);
    }
}

internal static class Converter
{
    public static readonly JsonSerializerSettings Settings = new()
    {
        MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
        Converters =
        {
            new IsoDateTimeConverter {DateTimeStyles = DateTimeStyles.AssumeUniversal}
        }
    };
}

internal class ParseStringConverter : JsonConverter
{
    public static readonly ParseStringConverter Singleton = new();

    public override bool CanConvert(Type t)
    {
        return t == typeof(long) || t == typeof(long?);
    }

    public override object? ReadJson(JsonReader reader, Type t, object existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;
        var value = serializer.Deserialize<string>(reader);
        long l;
        if (long.TryParse(value, out l)) return l;
        throw new Exception("Cannot unmarshal type long");
    }

    public override void WriteJson(JsonWriter writer, object untypedValue, JsonSerializer serializer)
    {
        if (untypedValue == null)
        {
            serializer.Serialize(writer, null);
            return;
        }

        var value = (long)untypedValue;
        serializer.Serialize(writer, value.ToString());
    }
}