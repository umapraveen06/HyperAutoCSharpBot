// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Recognizers.Text.DataTypes.TimexExpression;

namespace Microsoft.BotBuilderSamples.Dialogs
{
    public class ProjectStatusDialog : CancelAndHelpDialog
    {
        private const string ProjectStepMsgText = "Which Project Details you want?";
        


        public ProjectStatusDialog()
            : base(nameof(ProjectStatusDialog))
        {
            AddDialog(new TextPrompt(nameof(TextPrompt)));
            AddDialog(new ConfirmPrompt(nameof(ConfirmPrompt)));
            AddDialog(new DateResolverDialog());
            AddDialog(new WaterfallDialog(nameof(WaterfallDialog), new WaterfallStep[]
            {
                ProjectStepAsync,
                SuiteStepAsync,
                StatusStepAsync,
                ItemStepAsync,
                DateStepAsync,
                ConfirmStepAsync,
                FinalStepAsync,
            }));

            // The initial child Dialog to run.
            InitialDialogId = nameof(WaterfallDialog);
        }

        private async Task<DialogTurnResult> ProjectStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var ProjectDetails = (ProjectStatusDetails)stepContext.Options;

            if (ProjectDetails.Project == null)
            {
                var promptMessage = MessageFactory.Text(ProjectStepMsgText, ProjectStepMsgText, InputHints.ExpectingInput);
                return await stepContext.PromptAsync(nameof(TextPrompt), new PromptOptions { Prompt = promptMessage }, cancellationToken);
            }

            return await stepContext.NextAsync(ProjectDetails.Project, cancellationToken);
        }

        private async Task<DialogTurnResult> SuiteStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var ProjectDetails = (ProjectStatusDetails)stepContext.Options;
            ProjectDetails.Project = (string) stepContext.Result;
            var SuiteStepMsgText = $"Which Suite in {ProjectDetails.Project} Project you are looking for?";
            
            if (ProjectDetails.Suite == null)
            {
                var promptMessage = MessageFactory.Text(SuiteStepMsgText, SuiteStepMsgText, InputHints.ExpectingInput);
                return await stepContext.PromptAsync(nameof(TextPrompt), new PromptOptions { Prompt = promptMessage }, cancellationToken);
            }

            return await stepContext.NextAsync(ProjectDetails.Suite, cancellationToken);
        }

        private async Task<DialogTurnResult> StatusStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var ProjectDetails = (ProjectStatusDetails)stepContext.Options;
            ProjectDetails.Suite = (string) stepContext.Result;
            var StatusStepMsgText = $"Which Status in {ProjectDetails.Project} Project {ProjectDetails.Suite} Suite you are looking for?";
            if (ProjectDetails.Status == null)
            {
                var promptMessage = MessageFactory.Text(StatusStepMsgText, StatusStepMsgText, InputHints.ExpectingInput);
                return await stepContext.PromptAsync(nameof(TextPrompt), new PromptOptions { Prompt = promptMessage }, cancellationToken);
            }

            return await stepContext.NextAsync(ProjectDetails.Status, cancellationToken);
        }

        private async Task<DialogTurnResult> ItemStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var ProjectDetails = (ProjectStatusDetails)stepContext.Options;
            ProjectDetails.Status = (string) stepContext.Result;
            var ItemStepMsgText = $"Which Catogory in {ProjectDetails.Project} Project {ProjectDetails.Suite} Suite {ProjectDetails.Status} Status you are looking for?";
            if (ProjectDetails.Item == null)
            {
                var promptMessage = MessageFactory.Text(ItemStepMsgText, ItemStepMsgText, InputHints.ExpectingInput);
                return await stepContext.PromptAsync(nameof(TextPrompt), new PromptOptions { Prompt = promptMessage }, cancellationToken);
            }

            return await stepContext.NextAsync(ProjectDetails.Item, cancellationToken);
        }
        

        private async Task<DialogTurnResult> DateStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var ProjectDetails = (ProjectStatusDetails)stepContext.Options;
            ProjectDetails.Item = (string) stepContext.Result;
            if (ProjectDetails.Day == null || IsAmbiguous(ProjectDetails.Day))
            {
                return await stepContext.BeginDialogAsync(nameof(DateResolverDialog), ProjectDetails.Day, cancellationToken);
            }

            return await stepContext.NextAsync(ProjectDetails.Day, cancellationToken);
        }

        private async Task<DialogTurnResult> ConfirmStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var ProjectDetails = (ProjectStatusDetails)stepContext.Options;
            ProjectDetails.Day = (string) stepContext.Result;

            var messageText = $"Please confirm, you want to get the {ProjectDetails.Project} Project {ProjectDetails.Suite} Suite {ProjectDetails.Status} Status {ProjectDetails.Item} Category as on {ProjectDetails.Day}. Is this correct?";
            var promptMessage = MessageFactory.Text(messageText, messageText, InputHints.ExpectingInput);

            return await stepContext.PromptAsync(nameof(ConfirmPrompt), new PromptOptions { Prompt = promptMessage }, cancellationToken);
        }

        private async Task<DialogTurnResult> FinalStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if ((bool)stepContext.Result)
            {
                var bookingDetails = (ProjectStatusDetails)stepContext.Options;

                return await stepContext.EndDialogAsync(bookingDetails, cancellationToken);
            }

            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

        private static bool IsAmbiguous(string timex)
        {
            var timexProperty = new TimexProperty(timex);
            return !timexProperty.Types.Contains(Constants.TimexTypes.Definite);
        }
    }
}
