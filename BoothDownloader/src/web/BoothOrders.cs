namespace BoothDownloader.web;

public class BoothOrders
{
    private static bool _done;

    public static List<Items> Ordersloop()
    {
        List<Items> allItems = [];

        int pageNumber = 1;
        while (!_done)
        {
            List<Items>? items = OrdersParse(pageNumber).Result;
            allItems.AddRange(items);
            pageNumber++;
        }
        allItems.RemoveAll(s => string.IsNullOrWhiteSpace(s.Id));

        return allItems;
    }

    private static async Task<List<Items>> OrdersParse(int pageNumber)
    {
        List<Items> items = [];
        var boothclient = new BoothClient(BoothDownloader.Configextern.Config);
        var client = boothclient.MakeHttpClient();
        string url = $"https://accounts.booth.pm/orders?page={pageNumber}";
        HttpResponseMessage? response = await client.GetAsync(url);
        try
        {
            string content = await response.Content.ReadAsStringAsync();
            content = content.Replace("<", "\r\n<");
            string[] sheet = content.Split("<div class=\"sheet");
            if (!content.Contains("<div class=\"sheet"))
            {
                _done = true;
                return items;
            }
            foreach (string itemsheet in sheet)
            {
                Items item = new();
                StringReader reader = new(itemsheet);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line?.Contains("<a href=\"https://booth.pm/en/items/") == true)
                        item.Id = line.Split('/').Last().Split("\"").First();
                }
                items.Add(item);
            }
            return items;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"exception at method OrdersParse {exception}");
            throw;
        }
    }
}

public class Items
{
    public string Id = "";
}