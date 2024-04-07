using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

class CloudflareDnsUpdater
{
    private const string BaseUrl = "https://api.cloudflare.com/client/v4";
    private const string ApiToken = "API-TOKEN"; // Replace with your actual API token
    private const string OldIpAddress = "CurrentIP"; // The IP address to be updated
    private const string NewIpAddress = "NewIP"; // The new IP address
    private const string SkipPrefix = "ithilwen."; // dont update the ip of the server we need to access without proxy
    
    private static readonly HttpClient client = new HttpClient();
    private static readonly Dictionary<string, List<string>> changesSummary = new Dictionary<string, List<string>>();

    static async Task Main(string[] args)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiToken);

        var zones = await GetZones();
        foreach (var zone in zones)
        {
            changesSummary[zone.id] = new List<string>();

            var dnsRecords = await GetDnsRecords(zone.id);
            foreach (var record in dnsRecords)
            {
                if (record.content == OldIpAddress)
                {
                    await UpdateDnsRecord(zone.id, record.id, record.name, NewIpAddress, record.proxied, record.ttl);
                    changesSummary[zone.id].Add($"Updated {record.name} from {OldIpAddress} to {NewIpAddress}");
                }
            }
        }

        PrintSummary();
    }

    private static async Task<List<(string id, string name)>> GetZones()
    {
        var zones = new List<(string id, string name)>();
        int page = 0;
        bool hasMore = true;

        while (hasMore)
        {
            page++;
            var response = await client.GetAsync($"{BaseUrl}/zones?page={page}&per_page=50"); // Adjust per_page as needed
            var responseContent = await response.Content.ReadAsStringAsync();
            var jsonDocument = JsonDocument.Parse(responseContent);

            if (!jsonDocument.RootElement.GetProperty("success").GetBoolean())
            {
                throw new Exception("Failed to fetch zones");
            }

            foreach (var zone in jsonDocument.RootElement.GetProperty("result").EnumerateArray())
            {
                zones.Add((zone.GetProperty("id").GetString(), zone.GetProperty("name").GetString()));
            }

            // Check if there are more pages
            hasMore = jsonDocument.RootElement.GetProperty("result_info").GetProperty("total_pages").GetInt32() > page;
        }

        return zones;
    }


    private static async Task<List<(string id, string name, string content, bool proxied, int ttl)>> GetDnsRecords(string zoneId)
    {
        var records = new List<(string id, string name, string content, bool proxied, int ttl)>();
        var response = await client.GetAsync($"{BaseUrl}/zones/{zoneId}/dns_records");
        var responseContent = await response.Content.ReadAsStringAsync();
        var jsonDocument = JsonDocument.Parse(responseContent);

        foreach (var record in jsonDocument.RootElement.GetProperty("result").EnumerateArray())
        {
            records.Add(
                (
                    record.GetProperty("id").GetString(),
                    record.GetProperty("name").GetString(), 
                    record.GetProperty("content").GetString(), 
                    record.GetProperty("proxied").GetBoolean(), 
                    record.GetProperty("ttl").GetInt32()
                )
            );
        }

        return records;
    }

    private static async Task UpdateDnsRecord(string zoneId, string recordId, string name, string newIpAddress, bool proxied, int ttl)
    {
        bool doproxy = name.StartsWith(SkipPrefix) ? false : proxied;
        var data = new
        {
            type = "A",
            name = name,
            content = newIpAddress,
            ttl = ttl,
            proxied = doproxy
        };

        var json = JsonSerializer.Serialize(data);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await client.PutAsync($"{BaseUrl}/zones/{zoneId}/dns_records/{recordId}", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Updated DNS record {name} to {newIpAddress}");
        }
        else
        {
            Console.WriteLine($"Failed to update DNS record {name}: {responseContent}");
        }
    }

    private static void PrintSummary()
    {
        foreach (var zoneId in changesSummary.Keys)
        {
            if (changesSummary[zoneId].Count > 0)
            {
                Console.WriteLine($"Changes for Zone {zoneId}:");
                foreach (var change in changesSummary[zoneId])
                {
                    Console.WriteLine(change);
                }
            }
            else
            {
                Console.WriteLine($"No changes for Zone {zoneId}.");
            }
        }
    }
}
