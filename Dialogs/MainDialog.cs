// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.Recognizers.Text.DataTypes.TimexExpression;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
// using MathNet.Numerics.Statistics;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Text.Json;
using System.Collections.Generic;

namespace Microsoft.BotBuilderSamples.Dialogs
{
    public class MainDialog : ComponentDialog
    {
        private readonly ProjectStatusRecognizer _cluRecognizer;
        protected readonly ILogger Logger;

        // Dependency injection uses this constructor to instantiate MainDialog
        public MainDialog(ProjectStatusRecognizer cluRecognizer, ProjectStatusDialog projectStatusDialog, ILogger<MainDialog> logger)
            : base(nameof(MainDialog))
        {
            _cluRecognizer = cluRecognizer;
            Logger = logger;

            AddDialog(new TextPrompt(nameof(TextPrompt)));
            AddDialog(projectStatusDialog);
            AddDialog(new WaterfallDialog(nameof(WaterfallDialog), new WaterfallStep[]
            {
                IntroStepAsync,
                ActStepAsync,
                FinalStepAsync,
            }));

            // The initial child Dialog to run.
            InitialDialogId = nameof(WaterfallDialog);
        }

        private async Task<DialogTurnResult> IntroStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (!_cluRecognizer.IsConfigured)
            {
                await stepContext.Context.SendActivityAsync(
                    MessageFactory.Text("NOTE: CLU is not configured. To enable all capabilities, add 'CluProjectName', 'CluDeploymentName', 'CluAPIKey' and 'CluAPIHostName' to the appsettings.json file.", inputHint: InputHints.IgnoringInput), cancellationToken);

                return await stepContext.NextAsync(null, cancellationToken);
            }

            // Use the text provided in FinalStepAsync or the default if it is the first time.
            var weekLaterDate = DateTime.Now.AddDays(7).ToString("MMMM d, yyyy");
            var messageText = stepContext.Options?.ToString() ?? $"What can I help you with today?";
            var promptMessage = MessageFactory.Text(messageText, messageText, InputHints.ExpectingInput);
            return await stepContext.PromptAsync(nameof(TextPrompt), new PromptOptions { Prompt = promptMessage }, cancellationToken);
        }

        private async Task<DialogTurnResult> ActStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (!_cluRecognizer.IsConfigured)
            {
                // CLU is not configured, we just run the ProjectStatusDialog path with an empty BookingDetailsInstance.
                return await stepContext.BeginDialogAsync(nameof(ProjectStatusDialog), new ProjectStatusDetails(), cancellationToken);
            }

            // Call CLU and gather any potential booking details. (Note the TurnContext has the response to the prompt.)
            // var cluResult = await _cluRecognizer.RecognizeAsync<FlightBooking>(stepContext.Context, cancellationToken);
            var cluResult = await _cluRecognizer.RecognizeAsync<FlightBooking>(stepContext.Context, cancellationToken);
            Console.WriteLine("Top Intent below. ");
            Console.WriteLine("Top Intent: "+cluResult.GetTopIntent().intent);
            Console.WriteLine("Project: "+cluResult.Entities.GetProject());
            Console.WriteLine("Suite: "+cluResult.Entities.GetSuite());
            Console.WriteLine("Status: "+cluResult.Entities.GetStatus());
            Console.WriteLine("Item: "+cluResult.Entities.GetItem());
            Console.WriteLine("Day: "+cluResult.Entities.GetDay());

            switch (cluResult.GetTopIntent().intent)
            {
                case FlightBooking.Intent.count:
                    // Initialize ProjectStatusDetails with any entities we may have found in the response.

                    var projectStatus = new ProjectStatusDetails()
                    {
                        Project = cluResult.Entities.GetProject(),
                        Suite = cluResult.Entities.GetSuite(),
                        Status = cluResult.Entities.GetStatus(),
                        Day = cluResult.Entities.GetDay(),
                        Item = cluResult.Entities.GetItem(),
                    };


                    // Run the ProjectStatusDialog giving it whatever details we have from the CLU call, it will fill out the remainder.
                    return await stepContext.BeginDialogAsync(nameof(ProjectStatusDialog), projectStatus, cancellationToken);

                // case FlightBooking.Intent.GetWeather:
                //     // We haven't implemented the GetWeatherDialog so we just display a TODO message.
                //     var getWeatherMessageText = "TODO: get weather flow here";
                //     var getWeatherMessage = MessageFactory.Text(getWeatherMessageText, getWeatherMessageText, InputHints.IgnoringInput);
                //     await stepContext.Context.SendActivityAsync(getWeatherMessage, cancellationToken);
                //     break;

                default:
                    // Catch all for unhandled intents
                    var didntUnderstandMessageText = $"Sorry, I didn't get that. Please try asking in a different way (intent was {cluResult.GetTopIntent().intent})";
                    var didntUnderstandMessage = MessageFactory.Text(didntUnderstandMessageText, didntUnderstandMessageText, InputHints.IgnoringInput);
                    await stepContext.Context.SendActivityAsync(didntUnderstandMessage, cancellationToken);
                    break;
            }

            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> FinalStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // If the child dialog ("ProjectStatusDialog") was cancelled, the user failed to confirm or if the intent wasn't BookFlight
            // the Result here will be null.
            if (stepContext.Result is ProjectStatusDetails result)
            {
                // Now we have all the Project details call the Cognitive Search service.

                // var ProjectDetails = (ProjectStatusDetails)stepContext.Options;
                // Console.WriteLine($"Project Details: {ProjectDetails.Project}");
                string searchQuery = JsonSerializer.Serialize<ProjectStatusDetails>(result);
                Console.WriteLine(searchQuery);


                var queryString = "(project_name:\""+result.Project+"\")+(suite_description:\""+result.Suite+"\")";
                Console.WriteLine(queryString);

                // Get the service endpoint and API key from the environment
                Uri endpoint = new Uri("https://hyperautosearchservice.search.windows.net");
                string key = "mBxsjXw5SX5U7ekDXFgdViZqwiIXpBBYlD0K3VpSyyAzSeCfMIyh";
                // Create a client
                AzureKeyCredential credential = new AzureKeyCredential(key);
                SearchClient client = new SearchClient(endpoint, "azuresql-index", credential);
                SearchResults<SearchDocument> response = client.Search<SearchDocument>(queryString);
                // SearchResult<SearchDocument> result1 = response.GetResults();
                // Console.WriteLine($"After result Search call");
                
                var suitDescription = new List<string>();
                int passCount = 0;
                int failCount = 0;
                foreach (SearchResult<SearchDocument> searchResult in response.GetResults())
                {
                    var doc = searchResult.Document;
                    suitDescription.Add($"{doc["suite_description"]}");
                    if(String.Equals(doc["executions_status"], "Pass"))
                        passCount = passCount + 1;
                    if(String.Equals(doc["executions_status"], "Fail"))
                        failCount = failCount + 1;
                    Console.WriteLine($"{doc}");
                }
                var suitDescriptionJson = JsonSerializer.Serialize(suitDescription);
                // Console.WriteLine($"{suitDescriptionJson}");
                // int count = response.GetResults().Count;
                // Console.WriteLine($"{count}");
                // If the call to the Cognitive search service was successful tell the user.
                
                var messageText = $"The Results shown for {result.Project} Project {result.Suite} Suite {result.Status} Status {result.Item} Category as on {result.Day}";
                var message = MessageFactory.Text(messageText, messageText, InputHints.IgnoringInput);
                await stepContext.Context.SendActivityAsync(message, cancellationToken);


                messageText = $"Pass Count: {passCount},Fail Count: {failCount}";
                message = MessageFactory.Text(messageText, messageText, InputHints.IgnoringInput);
                await stepContext.Context.SendActivityAsync(message, cancellationToken);

                messageText = $"Suites: {suitDescriptionJson}";
                message = MessageFactory.Text(messageText, messageText, InputHints.IgnoringInput);
                await stepContext.Context.SendActivityAsync(message, cancellationToken);
            }

            // Restart the main dialog with a different message the second time around
            var promptMessage = "What else can I do for you?";
            return await stepContext.ReplaceDialogAsync(InitialDialogId, promptMessage, cancellationToken);
        }
    }
}
