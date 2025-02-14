﻿using Android;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Service.Autofill;
using Android.Widget;
using Bit.Core;
using Bit.Core.Abstractions;
using Bit.Core.Enums;
using Bit.Core.Utilities;
using System.Collections.Generic;
using System.Linq;

namespace Bit.Droid.Autofill
{
    [Service(Permission = Manifest.Permission.BindAutofillService, Label = "Bitwarden")]
    [IntentFilter(new string[] { "android.service.autofill.AutofillService" })]
    [MetaData("android.autofill", Resource = "@xml/autofillservice")]
    [Register("com.x8bit.bytegarden.Autofill.AutofillService")]
    public class AutofillService : Android.Service.Autofill.AutofillService
    {
        private ICipherService _cipherService;
        private ILockService _lockService;
        private IStorageService _storageService;

        public async override void OnFillRequest(FillRequest request, CancellationSignal cancellationSignal,
            FillCallback callback)
        {
            var structure = request.FillContexts?.LastOrDefault()?.Structure;
            if(structure == null)
            {
                return;
            }

            var parser = new Parser(structure, ApplicationContext);
            parser.Parse();

            if(_storageService == null)
            {
                _storageService = ServiceContainer.Resolve<IStorageService>("storageService");
            }

            var shouldAutofill = await parser.ShouldAutofillAsync(_storageService);
            if(!shouldAutofill)
            {
                return;
            }

            if(_lockService == null)
            {
                _lockService = ServiceContainer.Resolve<ILockService>("lockService");
            }

            List<FilledItem> items = null;
            var locked = await _lockService.IsLockedAsync();
            if(!locked)
            {
                if(_cipherService == null)
                {
                    _cipherService = ServiceContainer.Resolve<ICipherService>("cipherService");
                }
                items = await AutofillHelpers.GetFillItemsAsync(parser, _cipherService);
            }

            // build response
            var response = AutofillHelpers.BuildFillResponse(parser, items, locked);
            callback.OnSuccess(response);
        }

        public async override void OnSaveRequest(SaveRequest request, SaveCallback callback)
        {
            var structure = request.FillContexts?.LastOrDefault()?.Structure;
            if(structure == null)
            {
                return;
            }

            if(_storageService == null)
            {
                _storageService = ServiceContainer.Resolve<IStorageService>("storageService");
            }

            var disableSavePrompt = await _storageService.GetAsync<bool?>(Constants.AutofillDisableSavePromptKey);
            if(disableSavePrompt.GetValueOrDefault())
            {
                return;
            }

            var parser = new Parser(structure, ApplicationContext);
            parser.Parse();

            var savedItem = parser.FieldCollection.GetSavedItem();
            if(savedItem == null)
            {
                Toast.MakeText(this, "Unable to save this form.", ToastLength.Short).Show();
                return;
            }

            var intent = new Intent(this, typeof(MainActivity));
            intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
            intent.PutExtra("autofillFramework", true);
            intent.PutExtra("autofillFrameworkSave", true);
            intent.PutExtra("autofillFrameworkType", (int)savedItem.Type);
            switch(savedItem.Type)
            {
                case CipherType.Login:
                    intent.PutExtra("autofillFrameworkName", parser.Uri
                        .Replace(Constants.AndroidAppProtocol, string.Empty)
                        .Replace("https://", string.Empty)
                        .Replace("http://", string.Empty));
                    intent.PutExtra("autofillFrameworkUri", parser.Uri);
                    intent.PutExtra("autofillFrameworkUsername", savedItem.Login.Username);
                    intent.PutExtra("autofillFrameworkPassword", savedItem.Login.Password);
                    break;
                case CipherType.Card:
                    intent.PutExtra("autofillFrameworkCardName", savedItem.Card.Name);
                    intent.PutExtra("autofillFrameworkCardNumber", savedItem.Card.Number);
                    intent.PutExtra("autofillFrameworkCardExpMonth", savedItem.Card.ExpMonth);
                    intent.PutExtra("autofillFrameworkCardExpYear", savedItem.Card.ExpYear);
                    intent.PutExtra("autofillFrameworkCardCode", savedItem.Card.Code);
                    break;
                default:
                    Toast.MakeText(this, "Unable to save this type of form.", ToastLength.Short).Show();
                    return;
            }
            StartActivity(intent);
        }
    }
}
