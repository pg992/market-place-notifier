using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace TestStorageApp
{
    public class MarketPlaceNotifier
    {
        private readonly IConfiguration _configuration;

        public MarketPlaceNotifier(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [FunctionName("MarketPlaceNotify")]
        public async Task MarketPlaceNotify([TimerTrigger("0 */1 * * * *")]TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            var account = CloudStorageAccount.Parse(_configuration["AzureWebJobsStorage"]);
            var client = account.CreateCloudTableClient();
            var cloudTable = client.GetTableReference(_configuration["table"]);
            var date = DateTime.Now.AddMinutes(-int.Parse(_configuration["CronInterval"])).AddSeconds(-int.Parse(_configuration["CronThreshold"]));
            var result = await cloudTable.ExecuteQuerySegmentedAsync(new TableQuery<MarketPlaceEntity>()
                    .Where(TableQuery.GenerateFilterConditionForDate("Timestamp", QueryComparisons.GreaterThanOrEqual, date)), null)
                    .ConfigureAwait(false);
            var count = result.Count();

            if (count > 0)
            {
                var html = new StringBuilder();
                html.Append(@"
                    <html>
                        <head>
                            <style>
                                table {
                                    border-collapse: collapse;
                                    table-layout:fixed; 
                                    width:1000px;
                                }
            
                                table, td, th {
                                    border: 1px solid black;
                                }
                                table td {border:solid 1px #fab; width:100px; word-wrap:break-word;}
                            </style>
                        </head>");

                html.Append($@"
                    <body>
                        <h4>Dear {_configuration["SendGridToUser"]}</h4> 
                        <br>
                        <p>You have new Azure Market place Notification</p>
                        <p>Details:</p>                        
                        <br>
                        <table>
                            <thead>
                                <th>Lead Source</th>
                                <th>Name</th>
                                <th>Email</th>
                                <th>Phone</th>
                                <th>Country</th>
                                <th>Company</th>
                                <th>Title</th>
                                <th>Date</th>
                            </thead>
                            <tbody>");


                result.ToList().ForEach(r =>
                {
                    var ci = JsonConvert.DeserializeObject<CustomerInfo>(r.CustomerInfo);
                    html.Append($@"
                                    <tr>
                                        <td>{r.LeadSource}</td>
                                        <td>{ci.FirstName} {ci.LastName}</td>
                                        <td>{ci.Email}</td>
                                        <td>{ci.Phone}</td>
                                        <td>{ci.Country}</td>
                                        <td>{ci.Company}</td>
                                        <td>{ci.Title ?? ""}</td>
                                        <td>{r.Timestamp.UtcDateTime.ToString("dd MMMM yyyy HH:mm:ss")} UTC</td> 
                                    </tr> 
                    ");
                });
                html.Append($@"  </tbody>
                                </table>
                            </body>
                        </html>");

                var sendGridClient = new SendGridClient(_configuration["SendGridApiKey"]);
                var msg = new SendGridMessage();
                msg.SetFrom(new EmailAddress(_configuration["SendGridFromEmail"], _configuration["SendGridFromUser"]));
                msg.SetSubject(_configuration["SendGridSubject"]);
                msg.AddContent(MimeType.Html, html.ToString());
                msg.AddTo(new EmailAddress(_configuration["SendGridToEmail"], _configuration["SendGridToUser"]));
                var response = await sendGridClient.SendEmailAsync(msg).ConfigureAwait(false);
            }
        }
    }

    public class MarketPlaceEntity : TableEntity
    {
        public string CustomerInfo { get; set; }
        public string LeadSource { get; set; }
    }

    public class CustomerInfo
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Country { get; set; }
        public string Company { get; set; }
        public string Title { get; set; }
    }
}
