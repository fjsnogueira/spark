using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Spark.Core;
using Spark.Engine.Core;
using Spark.Engine.Extensions;
using Spark.Engine.Search;
using Spark.Engine.Store.Interfaces;
using Spark.Service;

namespace Spark.Engine.Service.FhirServiceExtensions
{
    public class SearchService : ISearchService, IServiceListener
    {
        private readonly IFhirModel fhirModel;
        private readonly ILocalhost localhost;
        private  IndexService indexService;
        private  IFhirIndex fhirIndex;

        public SearchService(ILocalhost localhost, IndexService indexService, IFhirIndex fhirIndex, IFhirModel fhirModel)
        {
            this.fhirModel = fhirModel;
            this.localhost = localhost;
            this.indexService = indexService;
            this.fhirIndex = fhirIndex;
        }

        public Snapshot GetSnapshot(string type, SearchParams searchCommand)
        {
            Validate.TypeName(type);
            SearchResults results =   fhirIndex.Search(type, searchCommand);

            if (results.HasErrors)
            {
                throw new SparkException(HttpStatusCode.BadRequest, results.Outcome);
            }

            UriBuilder builder = new UriBuilder(localhost.Uri(type));
            builder.Query = results.UsedParameters;
            Uri link = builder.Uri;

            Snapshot snapshot = CreateSnapshot(link, results, searchCommand);
            return snapshot;
        }

        public Snapshot GetSnapshotForEverything(IKey key)
        {
            var searchCommand = new SearchParams();
            searchCommand.Add("_id", key.ResourceId);
            var compartment = fhirModel.FindCompartmentInfo(key.TypeName);
            if (compartment != null)
            {
                foreach (var ri in compartment.ReverseIncludes)
                {
                    searchCommand.RevInclude.Add(ri);
                }
            }

            return GetSnapshot(key.TypeName, searchCommand);
        }

        private Snapshot CreateSnapshot(Uri selflink, IEnumerable<string> keys, SearchParams searchCommand)
        {
            string sort = GetFirstSort(searchCommand);

            int? count = searchCommand.Count;
            if (count.HasValue)
            {
                //TODO: should we change count?
                //count = Math.Min(searchCommand.Count.Value, MAX_PAGE_SIZE);
                selflink = selflink.AddParam(SearchParams.SEARCH_PARAM_COUNT, new string[] { count.ToString() });
            }

            if (String.IsNullOrEmpty(sort) == false)
            {
                selflink = selflink.AddParam(SearchParams.SEARCH_PARAM_SORT, new string[] { sort });
            }

            if (searchCommand.Include.Any())
            {
                selflink = selflink.AddParam(SearchParams.SEARCH_PARAM_INCLUDE, searchCommand.Include.ToArray());
            }

            if (searchCommand.RevInclude.Any())
            {
                selflink = selflink.AddParam(SearchParams.SEARCH_PARAM_REVINCLUDE, searchCommand.RevInclude.ToArray());
            }

            return Snapshot.Create(Bundle.BundleType.Searchset, selflink, keys, sort, count, searchCommand.Include, searchCommand.RevInclude);
        }

        private static string GetFirstSort(SearchParams searchCommand)
        {
            string firstSort = null;
            if (searchCommand.Sort != null && searchCommand.Sort.Any())
            {
                firstSort = searchCommand.Sort[0].Item1; //TODO: Support sortorder and multiple sort arguments.
            }
            return firstSort;
        }

        public IKey FindSingle(string type, SearchParams searchCommand)
        {
            throw new NotImplementedException();
        }

        public bool EnableForStore(IStorageBuilder builder)
        {
            fhirIndex = builder.GetFhirIndex();
            IIndexStore indexStore = builder.GetIndexStore();
            if (indexStore != null) //TODO: remove this when we can remove usages if IndexService
            {
                indexService = new IndexService(new FhirModel(), new FhirPropertyIndex(new FhirModel()),
                    new ResourceVisitor(new FhirPropertyIndex(new FhirModel())), new ElementIndexer(new FhirModel()),
                    indexStore);
            }
            return fhirIndex != null || indexService != null;
        }

        public void Inform(Uri location, Entry interaction)
        {
            if (indexService != null)
            {
                indexService.Process(interaction);
            }

            else if (fhirIndex != null)
            {
                //TODO: If IndexService is working correctly, remove the reference to fhirIndex.
                fhirIndex.Process(interaction);
            }
        }
    }
 
}