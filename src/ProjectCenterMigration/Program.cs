using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BD.Model;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace ProjectCenterMigration
{
    internal class Program
    {

        private static int Main(string[] args)
        {
            try
            {
                var dryRun = args.Any(a => string.Equals(a, "--dry-run", StringComparison.OrdinalIgnoreCase));
                var csvPath = args.FirstOrDefault(a => !a.StartsWith("-"));

                if (string.IsNullOrWhiteSpace(csvPath))
                {
                    Console.Error.WriteLine("CSV file not found. Expected a file named 'BC extract.csv' adjacent to the project root.");
                    return 1;
                }

                Console.WriteLine("Processing CSV: " + csvPath);
                var runner = new CsvMigrationRunner();
                var failedRows = runner.Run(csvPath, dryRun);
                if (failedRows > 0)
                {
                    Console.Error.WriteLine("Migration completed with " + failedRows + " failed row(s). See ProjectCenterMigration-errors.log.");
                    return 2;
                }
                Console.WriteLine("Migration completed successfully.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Migration failed.");
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }
    }

    internal sealed class CsvMigrationRunner
    {
        private const string ErrorLogFileName = "ProjectCenterMigration-errors.log";
        private const string SourceRowNumberKey = "__SourceRowNumber";
        private const string CsvTimeZoneId = "Singapore Standard Time";
        private const int ProgressInterval = 100;
        private const int DefaultBuildDegreeOfParallelism = 4;
        private static readonly object ErrorLogLock = new object();
        private static readonly TimeZoneInfo CsvTimeZone = TimeZoneInfo.FindSystemTimeZoneById(CsvTimeZoneId);

        private sealed class MigrationMetadataCache
        {
            private readonly IOrganizationService _service;
            private readonly Dictionary<string, RetrieveEntityResponse> _entityMetadata =
                new Dictionary<string, RetrieveEntityResponse>(StringComparer.OrdinalIgnoreCase);

            public MigrationMetadataCache(IOrganizationService service)
            {
                _service = service;
            }

            public RetrieveEntityResponse GetEntityMetadata(string entityName)
            {
                if (_entityMetadata.ContainsKey(entityName))
                {
                    return _entityMetadata[entityName];
                }

                var request = new RetrieveEntityRequest
                {
                    EntityFilters = EntityFilters.Attributes,
                    LogicalName = entityName
                };
                var response = (RetrieveEntityResponse)_service.Execute(request);
                _entityMetadata[entityName] = response;
                return response;
            }

            public DateTimeBehavior GetDateTimeBehavior(string entityName, string attributeName)
            {
                var attributes = GetEntityMetadata(entityName).EntityMetadata.Attributes;
                var attribute = attributes
                    .FirstOrDefault(item => string.Equals(item.LogicalName, attributeName, StringComparison.OrdinalIgnoreCase))
                    as DateTimeAttributeMetadata;
                if (attribute == null || attribute.DateTimeBehavior == null)
                {
                    throw new InvalidOperationException(
                        "Date/time metadata was not found for " + entityName + "." + attributeName + ".");
                }

                return attribute.DateTimeBehavior;
            }

            private readonly Dictionary<string, ProcessStage[]> _bpfStages =
                new Dictionary<string, ProcessStage[]>(StringComparer.OrdinalIgnoreCase);

            // Loaded once at startup so worker threads only read the cache.
            public void LoadBpfStages(string bpfEntityName)
            {
                var processQuery = new QueryExpression("workflow")
                {
                    ColumnSet = new ColumnSet("workflowid"),
                    TopCount = 1
                };
                processQuery.Criteria.AddCondition("uniquename", ConditionOperator.Equal, bpfEntityName);
                processQuery.Criteria.AddCondition("category", ConditionOperator.Equal, 4);
                processQuery.Criteria.AddCondition("type", ConditionOperator.Equal, 1);

                var process = _service.RetrieveMultiple(processQuery).Entities.FirstOrDefault();
                if (process == null)
                {
                    throw new InvalidOperationException("Business process flow definition '" + bpfEntityName + "' was not found.");
                }

                var stageQuery = new QueryExpression(ProcessStage.EntityLogicalName)
                {
                    ColumnSet = new ColumnSet(ProcessStage.Fields.StageName, ProcessStage.Fields.ParentProcessStageId)
                };
                stageQuery.Criteria.AddCondition(ProcessStage.Fields.ProcessId, ConditionOperator.Equal, process.Id);

                _bpfStages[bpfEntityName] = _service.RetrieveMultiple(stageQuery).Entities
                    .Select(e => e.ToEntity<ProcessStage>())
                    .ToArray();
            }

            public ProcessStage[] GetBpfStages(string bpfEntityName)
            {
                return _bpfStages[bpfEntityName];
            }
        }

        private sealed class PendingCreate
        {
            public string Description { get; set; }

            public Entity Entity { get; set; }
        }

        private sealed class PendingRequestIntake
        {
            public string Ticket { get; set; }

            public int RowNumber { get; set; }

            public Dictionary<string, string> Row { get; set; }

            public bdgas_requestintake Entity { get; set; }

            public bool IsCreate { get; set; }

            public bool ShouldDeactivateAfterCreate { get; set; }

            public bdgas_requestintake_statecode FinalStateCode { get; set; }

            public bdgas_requestintake_statuscode? FinalStatusCode { get; set; }
        }

        public int Run(string csvPath, bool dryRun)
        {
            var rows = ReadCsv(csvPath);
            if (rows.Count == 0)
            {
                throw new InvalidOperationException("No data rows were found in the CSV file.");
            }

            var connectionString = ResolveConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                if (dryRun)
                {
                    Console.WriteLine("No Dataverse connection string found. Running in dry-run mode.");
                    foreach (var row in rows)
                    {
                        Console.WriteLine("Dry run: would process ticket " + GetValue(row, "ServiceNow Ticket#"));
                    }
                    return 0;
                }

                throw new InvalidOperationException("No Dataverse connection string found. Set CRM_CONNECTION_STRING or DATAVERSE_CONNECTION_STRING before running the migration.");
            }

            using (var client = CreateReadyServiceClient(connectionString))
            {
                Console.WriteLine("Connected to Dataverse.");

                var metadata = new MigrationMetadataCache(client);
                metadata.GetEntityMetadata(bdgas_requestintake.EntityLogicalName);
                metadata.GetEntityMetadata(bdgas_budgetrequest.EntityLogicalName);
                metadata.GetEntityMetadata(bdgas_BudgetRequestHistory.EntityLogicalName);
                metadata.LoadBpfStages(bdgas_qpabpf.EntityLogicalName);
                metadata.LoadBpfStages(bdgas_rpabpf.EntityLogicalName);

                var validRows = rows.Where(row => !string.IsNullOrWhiteSpace(GetValue(row, "ServiceNow Ticket#"))).ToList();
                return ProcessRowsInParallel(validRows, dryRun, () => CreateReadyServiceClient(connectionString), metadata);
            }
        }

        private static ServiceClient CreateReadyServiceClient(string connectionString)
        {
            var client = new ServiceClient(connectionString);
            if (!client.IsReady)
            {
                client.Dispose();
                throw new InvalidOperationException("Dataverse connection was not established successfully. Check the configured connection string and credentials.");
            }

            return client;
        }

        private static int ProcessRowsInParallel(
            List<Dictionary<string, string>> rows,
            bool dryRun,
            Func<IOrganizationService> createService,
            MigrationMetadataCache metadata)
        {
            var degreeOfParallelism = ResolveBuildDegreeOfParallelism();
            Console.WriteLine("Processing Request Intakes, budget requests, and history using " + degreeOfParallelism + " worker(s) and individual requests.");

            var processedRows = 0;
            var failedRows = 0;
            var workerClients = new ThreadLocal<IOrganizationService>(createService, true);
            try
            {
                Parallel.ForEach(
                    rows.GroupBy(row => GetValue(row, "ServiceNow Ticket#").Trim(), StringComparer.OrdinalIgnoreCase),
                    new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism },
                    ticketRows =>
                    {
                        foreach (var row in ticketRows)
                        {
                            try
                            {
                                var service = workerClients.Value;
                                var pendingRequestIntake = BuildRequestIntake(service, row, metadata);
                                ProcessRequestIntake(service, pendingRequestIntake, dryRun, metadata);
                            }
                            catch (Exception ex)
                            {
                                Interlocked.Increment(ref failedRows);
                                lock (ErrorLogLock)
                                {
                                    LogDataverseError("Failed ticket " + GetValue(row, "ServiceNow Ticket#").Trim()
                                        + " from CSV row " + GetRowNumber(row)
                                        + ". Skipping the remaining operations for this row and continuing with other rows. Earlier writes for this row are not rolled back.");
                                    LogDataverseException(ex);
                                }
                            }
                            var currentProcessedRows = Interlocked.Increment(ref processedRows);
                            if (currentProcessedRows % ProgressInterval == 0 || currentProcessedRows == rows.Count)
                            {
                                Console.WriteLine("Processed " + currentProcessedRows + " of " + rows.Count + " rows.");
                            }
                        }
                    });
            }
            finally
            {
                foreach (var workerClient in workerClients.Values)
                {
                    var disposableClient = workerClient as IDisposable;
                    if (disposableClient != null)
                    {
                        disposableClient.Dispose();
                    }
                }

                workerClients.Dispose();
            }

            Console.WriteLine("Migration summary: attempted " + processedRows + " rows; succeeded "
                + (processedRows - failedRows) + "; failed " + failedRows + ".");
            if (failedRows > 0)
            {
                Console.Error.WriteLine("Row failure details: " + Path.GetFullPath(ErrorLogFileName));
            }
            return failedRows;
        }

        private static int ResolveBuildDegreeOfParallelism()
        {
            var configuredValue = Environment.GetEnvironmentVariable("PROJECT_CENTER_BUILD_THREADS");
            int configuredThreads;
            if (int.TryParse(configuredValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out configuredThreads) && configuredThreads > 0)
            {
                return configuredThreads;
            }

            return Math.Max(1, Math.Min(DefaultBuildDegreeOfParallelism, Environment.ProcessorCount));
        }

        private static string ResolveConnectionString()
        {
            var configuredConnectionString = ConfigurationManager.ConnectionStrings["Dataverse"];
            if (configuredConnectionString != null && !string.IsNullOrWhiteSpace(configuredConnectionString.ConnectionString))
            {
                return configuredConnectionString.ConnectionString;
            }

            var candidates = new[]
            {
                Environment.GetEnvironmentVariable("CRM_CONNECTION_STRING"),
                Environment.GetEnvironmentVariable("DATAVERSE_CONNECTION_STRING"),
                Environment.GetEnvironmentVariable("PROJECT_CENTER_CONNECTION_STRING")
            };

            return candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }

        private static List<Dictionary<string, string>> ReadCsv(string filePath)
        {
            var records = new List<Dictionary<string, string>>();
            using (var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(filePath, Encoding.UTF8))
            {
                parser.TextFieldType = Microsoft.VisualBasic.FileIO.FieldType.Delimited;
                parser.SetDelimiters(",");
                parser.HasFieldsEnclosedInQuotes = true;

                if (parser.EndOfData)
                {
                    return records;
                }

                var headers = parser.ReadFields();
                var csvRowNumber = 1;
                while (!parser.EndOfData)
                {
                    csvRowNumber++;
                    var values = parser.ReadFields();
                    if (values == null || values.Length == 0)
                    {
                        continue;
                    }

                    var record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (var j = 0; j < headers.Length; j++)
                    {
                        var key = headers[j].Trim();
                        var value = j < values.Length ? values[j] : string.Empty;
                        record[key] = value;
                    }

                    if (!record.All(kvp => string.IsNullOrWhiteSpace(kvp.Value)))
                    {
                        record[SourceRowNumberKey] = csvRowNumber.ToString(CultureInfo.InvariantCulture);
                        records.Add(record);
                    }
                }
            }

            Console.WriteLine("Read " + records.Count + " data rows from CSV.");
            return records;
        }

        private static PendingRequestIntake BuildRequestIntake(
            IOrganizationService service,
            Dictionary<string, string> row,
            MigrationMetadataCache metadata)
        {
            var ticket = GetValue(row, "ServiceNow Ticket#").Trim();
            var rowNumber = GetRowNumber(row);
            if (string.IsNullOrWhiteSpace(ticket))
            {
                return null;
            }

            var existing = FindRequestIntakeByTicket(service, ticket);
            var entity = new bdgas_requestintake();

            entity.bdgas_requestintake1 = ticket;
            entity.bdgas_digitalproductname = GetString(row, "Title");
            entity.bdgas_botowner = FindSystemUserLookup(service, GetValue(row, "Bot Owner"));
            entity.bdgas_leaddeveloperid = FindSystemUserLookup(service, GetFirstDeveloperName(row));
            entity.bdgas_developmentteamid = FindVendorLookup(service, GetValue(row, "Development Team"));
            entity.bdgas_assigneeba = FindSystemUserLookup(service, GetValue(row, "Assignee"));
            entity.bdgas_amosupportteamid = FindVendorLookup(service, GetValue(row, "AMO Support Team"));
            entity.bdgas_businesspartner = FindSystemUserLookup(service, GetValue(row, "Business Partner"));
            entity.bdgas_botvp = FindSystemUserLookup(service, GetValue(row, "Bot VP"));
            entity.bdgas_changeticket = GetString(row, "Change Ticket #");
            entity.bdgas_description = GetString(row, "Description");
            entity.bdgas_FTEBenefit = GetValue(row, "FTE Benefit Monthly Hours");
            entity.bdgas_autogenerateserviceaccount = GetString(row, "Service Account(s) Used");
            entity.bdgas_serviceaccountused = ParseNullableBool(GetValue(row, "Service Account(s) Used")) ?? false;
            entity.bdgas_prodcodereleasedate = ParseCsvDate(
                GetValue(row, "PROD Code Release Date"),
                metadata,
                bdgas_requestintake.EntityLogicalName,
                bdgas_requestintake.Fields.bdgas_prodcodereleasedate);
            entity.bdgas_uatcompletedate = ParseCsvDate(
                GetValue(row, "UAT Complete Date"),
                metadata,
                bdgas_requestintake.EntityLogicalName,
                bdgas_requestintake.Fields.bdgas_uatcompletedate);
            entity.bdgas_planviewid = GetString(row, "PlanView ID");
            entity.bdgas_prodlink = GetString(row, "Automation hub resource link");
            entity.bdgas_kttoamo = ParseNullableBool(GetValue(row, "KT To Ops"));
            entity.bdgas_botinformedid = FindSystemUserLookup(service, GetFirstBotInformedName(row));
            entity.bdgas_botisinhypercare1 = ParseNullableBool(GetValue(row, "Bot is in Hypercare?")).GetValueOrDefault() ? bdgas_requestintake_bdgas_botisinhypercare1.Yes : bdgas_requestintake_bdgas_botisinhypercare1.No;
            entity.bdgas_botbusinesscriticality = MapBotBusinessCriticality(GetValue(row, "Bot Business Criticality"));
            entity.bdgas_l1option = MapL1Option(GetValue(row, "L1"));
            entity.bdgas_tcodes = GetString(row, "T-Codes");
            entity.bdgas_automationursreceivedattached = ParseNullableBool(GetValue(row, "Automation URS Received & Attached"));
            entity.bdgas_enhancementofexistingbot = ParseNullableBool(GetValue(row, "Enhancement of Existing Bot?"));
            var lifecyclePhase = GetValue(row, "Lifecycle Phase");
            var statusCode = MapLifecyclePhaseToStatusCode(lifecyclePhase);
            var bootIsActive = ParseBool(GetValue(row, "Bot is Active?"));
            var resolvedStatusCode = !bootIsActive && !IsInactiveStatus(statusCode)
                ? bdgas_requestintake_statuscode.RetiredandRetained
                : statusCode;
            var stateCode = MapStatusCodeToStateCode(resolvedStatusCode, bootIsActive);
            entity.statecode = stateCode;
            entity.statuscode = resolvedStatusCode;

            Console.WriteLine("Resolved lifecycle '" + lifecyclePhase + "' to statecode " + (int)entity.statecode + " and statuscode " + (int)entity.statuscode.Value + " for " + ticket + " from CSV row " + rowNumber);

            var createdOn = ParseCsvDate(
                GetValue(row, "RITM Creation Date"),
                metadata,
                bdgas_requestintake.EntityLogicalName,
                bdgas_requestintake.Fields.OverriddenCreatedOn);
            if (existing == null && createdOn.HasValue)
            {
                entity.OverriddenCreatedOn = createdOn.Value;
            }

            var devSizeRef = GetReferenceForCategoryAndName(service, bdgas_regulatoryassessmentmaster.EntityLogicalName, bdgas_regulatoryassessmentmaster.Fields.bdgas_name, bdgas_regulatoryassessmentmaster.Fields.bdgas_category, "Development Size", GetValue(row, "Development Size"));
            if (devSizeRef != null)
            {
                entity.bdgas_developmentsizeid = devSizeRef;
            }

            var regionRef = GetReferenceForCategoryAndName(service, bdgas_regulatoryassessmentmaster.EntityLogicalName, bdgas_regulatoryassessmentmaster.Fields.bdgas_name, bdgas_regulatoryassessmentmaster.Fields.bdgas_category, "Region", GetValue(row, "Region"));
            if (regionRef != null)
            {
                entity.bdgas_bdintelligenceregion = regionRef;
            }

            var functionRef = GetReferenceForCategoryAndName(service, bdgas_regulatoryassessmentmaster.EntityLogicalName, bdgas_regulatoryassessmentmaster.Fields.bdgas_name, bdgas_regulatoryassessmentmaster.Fields.bdgas_category, "Function", GetValue(row, "Function"));
            if (functionRef != null)
            {
                entity.bdgas_bdintelligencefunction = functionRef;
            }

            var fteMonthlyHours = ParseNullableDecimal(GetValue(row, "FTE Benefit Monthly Hours"));
            if (fteMonthlyHours.HasValue)
            {
                entity.bdgas_ftebenefitmonthlyhours = fteMonthlyHours.Value;
            }

            var estimatedHours = ParseNullableInt(GetValue(row, "FTE Benefit"));
            if (estimatedHours.HasValue)
            {
                entity.bdgas_estimatedhours = estimatedHours.Value;
            }

            if (existing == null)
            {
                var shouldDeactivateAfterCreate = entity.statecode == bdgas_requestintake_statecode.Inactive;
                if (shouldDeactivateAfterCreate)
                {
                    entity.statecode = bdgas_requestintake_statecode.Active;
                    entity.statuscode = bdgas_requestintake_statuscode.InQueue;
                }

                return new PendingRequestIntake
                {
                    Ticket = ticket,
                    RowNumber = rowNumber,
                    Row = row,
                    Entity = entity,
                    IsCreate = true,
                    ShouldDeactivateAfterCreate = shouldDeactivateAfterCreate,
                    FinalStateCode = stateCode,
                    FinalStatusCode = resolvedStatusCode
                };
            }

            entity.Id = existing.Id;
            return new PendingRequestIntake
            {
                Ticket = ticket,
                RowNumber = rowNumber,
                Row = row,
                Entity = entity,
                IsCreate = false,
                ShouldDeactivateAfterCreate = false,
                FinalStateCode = stateCode,
                FinalStatusCode = resolvedStatusCode
            };
        }

        private static void CreateChargebackRows(
            IOrganizationService service,
            Dictionary<string, string> row,
            Guid requestIntakeId,
            bool dryRun,
            MigrationMetadataCache metadata)
        {
            var pendingCreates = new List<PendingCreate>();
            AddChargebackRowCreates(pendingCreates, service, row, requestIntakeId, dryRun, metadata);

            if (pendingCreates.Count > 0)
            {
                CreateBudgetRequestsWithHistory(service, pendingCreates, metadata);
            }
        }

        private static void CreateBudgetRequestsWithHistory(
            IOrganizationService service,
            List<PendingCreate> pendingCreates,
            MigrationMetadataCache metadata)
        {
            var botOwnersByIntake = new Dictionary<Guid, EntityReference>();
            var processedBudgetRequestIds = new HashSet<Guid>();
            foreach (var pendingCreate in pendingCreates)
            {
                var budgetRequest = (bdgas_budgetrequest)pendingCreate.Entity;
                if (budgetRequest.Id == Guid.Empty)
                {
                    budgetRequest.Id = CreateWithoutCustomPlugins(service, budgetRequest, pendingCreate.Description);
                }

                if (!processedBudgetRequestIds.Add(budgetRequest.Id))
                {
                    continue;
                }

                if (FindExistingBudgetRequestHistory(service, budgetRequest.Id) != null)
                {
                    Console.WriteLine("Skipped existing budget request history for budget request " + budgetRequest.Id + ".");
                    continue;
                }

                EntityReference botOwner;
                if (!botOwnersByIntake.TryGetValue(budgetRequest.bdgas_requestintakeid.Id, out botOwner))
                {
                    var requestIntake = service.Retrieve(
                        bdgas_requestintake.EntityLogicalName,
                        budgetRequest.bdgas_requestintakeid.Id,
                        new ColumnSet(bdgas_requestintake.Fields.bdgas_botowner)).ToEntity<bdgas_requestintake>();
                    botOwner = requestIntake.bdgas_botowner;
                    botOwnersByIntake[requestIntake.Id] = botOwner;
                }

                var history = new bdgas_BudgetRequestHistory
                {
                    bdgas_projectcenterid = budgetRequest.bdgas_requestintakeid,
                    bdgas_budgetrequest = budgetRequest.ToEntityReference(),
                    bdgas_fee = budgetRequest.bdgas_fee,
                    bdgas_financepoc = budgetRequest.bdgas_financepoc,
                    bdgas_botowner = botOwner,
                    bdgas_approveddate = ConvertDateForBehavior(
                        DateTime.UtcNow,
                        metadata,
                        bdgas_BudgetRequestHistory.EntityLogicalName,
                        bdgas_BudgetRequestHistory.Fields.bdgas_approveddate),
                    bdgas_costcenter = budgetRequest.bdgas_costcenter
                };
                history.Id = CreateWithoutCustomPlugins(service, history, pendingCreate.Description + " history");
                UpdateWithoutCustomPlugins(service, new bdgas_BudgetRequestHistory
                {
                    Id = history.Id,
                    statecode = bdgas_budgetrequesthistory_statecode.Inactive,
                    statuscode = bdgas_budgetrequesthistory_statuscode.Approved
                }, pendingCreate.Description + " history approval");
            }
        }

        private static void AddChargebackRowCreates(
            List<PendingCreate> pendingCreates,
            IOrganizationService service,
            Dictionary<string, string> row,
            Guid requestIntakeId,
            bool dryRun,
            MigrationMetadataCache metadata)
        {
            var financePocLookup = FindSystemUserLookup(service, GetValue(row, "Finance POC"));

            var firstFee = ParseNullableDecimal(GetValue(row, "Fee - 1st chargeback - 50% Dev"));
            if (firstFee.HasValue && firstFee.Value > 0)
            {
                AddBudgetRequestCreate(pendingCreates, service, requestIntakeId, dryRun, "1st chargeback from CSV row " + GetRowNumber(row), GetValue(row, "Date - 1st chargeback - 50% Dev"), firstFee.Value, GetValue(row, "Cost Center DEV"), GetValue(row, "Chargeback Comments"), financePocLookup, false, bdgas_chargebacktype.Development, null, metadata);
            }

            var secondFee = ParseNullableDecimal(GetValue(row, "Fee - 2nd chargeback - 50% Dev and Operation"));
            if (secondFee.HasValue && secondFee.Value > 0)
            {
                AddBudgetRequestCreate(pendingCreates, service, requestIntakeId, dryRun, "2nd chargeback from CSV row " + GetRowNumber(row), GetValue(row, "Date - 2nd chargeback - 50% Dev and Operation"), secondFee.Value, GetValue(row, "Cost Center DEV"), GetValue(row, "Chargeback Comments"), financePocLookup, false, bdgas_chargebacktype.Development, null, metadata);
            }

            var annualFee = ParseNullableDecimal(GetValue(row, "Fee - Annual"));
            if (annualFee.HasValue && annualFee.Value > 0)
            {
                AddBudgetRequestCreate(pendingCreates, service, requestIntakeId, dryRun, "annual chargeback from CSV row " + GetRowNumber(row), null, annualFee.Value, GetValue(row, "Cost Center Reoccurring"), GetValue(row, "Chargeback Comments"), financePocLookup, true, bdgas_chargebacktype.Operations, ParseCsvDate(
                    GetValue(row, "Next Chargeback date"),
                    metadata,
                    bdgas_budgetrequest.EntityLogicalName,
                    bdgas_budgetrequest.Fields.bdgas_nextchargebackdate), metadata);
            }
        }

        private static void AddBudgetRequestCreate(
            List<PendingCreate> pendingCreates,
            IOrganizationService service,
            Guid requestIntakeId,
            bool dryRun,
            string description,
            string dateValue,
            decimal feeAmount,
            string costCenter,
            string chargebackComment,
            EntityReference financePoc,
            bool isRecurring,
            bdgas_chargebacktype chargebackType,
            DateTime? nextChargebackDate,
            MigrationMetadataCache metadata)
        {
            var existing = dryRun ? null : FindExistingBudgetRequest(service, requestIntakeId, feeAmount, costCenter, chargebackComment, financePoc, isRecurring, chargebackType);
            if (existing != null)
            {
                Console.WriteLine("Skipped existing budget request for intake " + requestIntakeId + "; checking its history.");
            }

            var entity = new bdgas_budgetrequest
            {
                bdgas_requestintakeid = new EntityReference(bdgas_requestintake.EntityLogicalName, requestIntakeId),
                bdgas_fee = new Money(feeAmount),
                bdgas_costcenter = costCenter,
                bdgas_name = chargebackComment,
                bdgas_financepoc = financePoc,
                bdgas_isrecurring = isRecurring,
                bdgas_chargebacktype = chargebackType,
                statecode = bdgas_budgetrequest_statecode.Active,
                statuscode = bdgas_budgetrequest_statuscode.Verified
            };
            if (existing != null)
            {
                entity.Id = existing.Id;
            }

            var parsedDate = ParseCsvDate(
                dateValue,
                metadata,
                bdgas_budgetrequest.EntityLogicalName,
                bdgas_budgetrequest.Fields.bdgas_datechargeback);
            if (parsedDate.HasValue)
            {
                entity.bdgas_datechargeback = parsedDate.Value;
            }

            if (nextChargebackDate.HasValue)
            {
                entity.bdgas_nextchargebackdate = nextChargebackDate.Value;
            }

            if (dryRun)
            {
                Console.WriteLine("Dry run: would create " + description + " verified budget request and approved history for intake " + requestIntakeId);
                return;
            }

            pendingCreates.Add(new PendingCreate
            {
                Description = description + " budget request for intake " + requestIntakeId,
                Entity = entity
            });
        }

        private static bdgas_budgetrequest FindExistingBudgetRequest(
            IOrganizationService service,
            Guid requestIntakeId,
            decimal feeAmount,
            string costCenter,
            string chargebackComment,
            EntityReference financePoc,
            bool isRecurring,
            bdgas_chargebacktype chargebackType)
        {
            var query = new QueryExpression(bdgas_budgetrequest.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1
            };

            query.Criteria.AddCondition(
                bdgas_budgetrequest.Fields.bdgas_requestintakeid,
                ConditionOperator.Equal,
                requestIntakeId);
            query.Criteria.AddCondition(bdgas_budgetrequest.Fields.bdgas_fee, ConditionOperator.Equal, feeAmount);
            query.Criteria.AddCondition(bdgas_budgetrequest.Fields.bdgas_isrecurring, ConditionOperator.Equal, isRecurring);
            query.Criteria.AddCondition(bdgas_budgetrequest.Fields.bdgas_chargebacktype, ConditionOperator.Equal, (int)chargebackType);
            AddTextMatchCondition(query, bdgas_budgetrequest.Fields.bdgas_costcenter, costCenter);
            AddTextMatchCondition(query, bdgas_budgetrequest.Fields.bdgas_name, chargebackComment);

            if (financePoc == null)
            {
                query.Criteria.AddCondition(bdgas_budgetrequest.Fields.bdgas_financepoc, ConditionOperator.Null);
            }
            else
            {
                query.Criteria.AddCondition(bdgas_budgetrequest.Fields.bdgas_financepoc, ConditionOperator.Equal, financePoc.Id);
            }

            var entity = service.RetrieveMultiple(query).Entities.FirstOrDefault();
            return entity == null ? null : entity.ToEntity<bdgas_budgetrequest>();
        }

        private static bdgas_BudgetRequestHistory FindExistingBudgetRequestHistory(IOrganizationService service, Guid budgetRequestId)
        {
            var query = new QueryExpression(bdgas_BudgetRequestHistory.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1
            };
            query.Criteria.AddCondition(bdgas_BudgetRequestHistory.Fields.bdgas_budgetrequest, ConditionOperator.Equal, budgetRequestId);

            var entity = service.RetrieveMultiple(query).Entities.FirstOrDefault();
            return entity == null ? null : entity.ToEntity<bdgas_BudgetRequestHistory>();
        }

        private static void AddTextMatchCondition(QueryExpression query, string fieldName, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                query.Criteria.AddCondition(fieldName, ConditionOperator.Null);
                return;
            }

            query.Criteria.AddCondition(fieldName, ConditionOperator.Equal, value.Trim());
        }

        private static void ProcessRequestIntake(
            IOrganizationService service,
            PendingRequestIntake operation,
            bool dryRun,
            MigrationMetadataCache metadata)
        {
            if (dryRun)
            {
                Console.WriteLine("Dry run: would upsert Request Intake " + operation.Ticket + " from CSV row " + operation.RowNumber);
                CreateChargebackRows(service, operation.Row, Guid.Empty, true, metadata);
                return;
            }

            var description = "Request Intake " + operation.Ticket + " from CSV row " + operation.RowNumber;
            if (operation.IsCreate)
            {
                operation.Entity.Id = CreateWithoutCustomPlugins(service, operation.Entity, description);
                Console.WriteLine("Created " + description + " (" + operation.Entity.Id + ")");
            }
            else
            {
                UpdateWithoutCustomPlugins(service, operation.Entity, description);
                Console.WriteLine("Updated " + description + " (" + operation.Entity.Id + ")");
            }

            if (operation.ShouldDeactivateAfterCreate)
            {
                UpdateWithoutCustomPlugins(service, new bdgas_requestintake
                {
                    Id = operation.Entity.Id,
                    statecode = operation.FinalStateCode,
                    statuscode = operation.FinalStatusCode
                }, description + " final state");
            }

            SyncBusinessProcessFlow(
                service,
                operation.Entity.Id,
                operation.FinalStatusCode,
                operation.Entity.bdgas_botisinhypercare1 == bdgas_requestintake_bdgas_botisinhypercare1.Yes,
                metadata,
                description);

            CreateChargebackRows(service, operation.Row, operation.Entity.Id, false, metadata);
        }

        // Mirrors BD.IPA.Plugins RequestIntake.SetBpfStatus, which does not run because custom plugins are bypassed.
        private static void SyncBusinessProcessFlow(
            IOrganizationService service,
            Guid requestIntakeId,
            bdgas_requestintake_statuscode? statusCode,
            bool isHypercare,
            MigrationMetadataCache metadata,
            string description)
        {
            if (statusCode == null) return;

            var requestIntake = service.Retrieve(
                bdgas_requestintake.EntityLogicalName,
                requestIntakeId,
                new ColumnSet(bdgas_requestintake.Fields.bdgas_RequestIntakeType)).ToEntity<bdgas_requestintake>();
            var isQpa = requestIntake.bdgas_RequestIntakeType == bdgas_requestintake_bdgas_requestintaketype.QPA;
            var bpfEntityName = isQpa ? bdgas_qpabpf.EntityLogicalName : bdgas_rpabpf.EntityLogicalName;

            var stageName = MapStatusCodeToBpfStageName(statusCode.Value, isHypercare, isQpa);
            var stages = metadata.GetBpfStages(bpfEntityName);
            var selectedStage = stages.FirstOrDefault(stage => string.Equals(stage.StageName, stageName, StringComparison.OrdinalIgnoreCase));
            if (selectedStage == null)
            {
                Console.WriteLine("BPF stage '" + stageName + "' was not found in " + bpfEntityName + " for " + description + ". Skipping BPF update.");
                return;
            }

            var bpfDescription = bpfEntityName + " for " + description;
            var existingQuery = new QueryExpression(bpfEntityName)
            {
                ColumnSet = new ColumnSet("statecode"),
                TopCount = 1
            };
            existingQuery.Criteria.AddCondition("bpf_bdgas_requestintakeid", ConditionOperator.Equal, requestIntakeId);
            var existing = service.RetrieveMultiple(existingQuery).Entities.FirstOrDefault();

            var bpf = new Entity(bpfEntityName);
            bpf["activestageid"] = selectedStage.ToEntityReference();
            bpf["traversedpath"] = BuildTraversedPath(stages, selectedStage);

            if (existing == null)
            {
                bpf["bpf_bdgas_requestintakeid"] = new EntityReference(bdgas_requestintake.EntityLogicalName, requestIntakeId);
                CreateWithoutCustomPlugins(service, bpf, bpfDescription);
            }
            else
            {
                var state = existing.GetAttributeValue<OptionSetValue>("statecode");
                if (state != null && state.Value != 0)
                {
                    var reactivate = new Entity(bpfEntityName, existing.Id);
                    reactivate["statecode"] = new OptionSetValue(0);
                    reactivate["statuscode"] = new OptionSetValue(1);
                    UpdateWithoutCustomPlugins(service, reactivate, bpfDescription + " reactivate");
                }

                bpf.Id = existing.Id;
                UpdateWithoutCustomPlugins(service, bpf, bpfDescription);
            }

            Console.WriteLine("Set " + bpfEntityName + " stage '" + selectedStage.StageName + "' for " + description);
        }

        private static string MapStatusCodeToBpfStageName(bdgas_requestintake_statuscode statusCode, bool isHypercare, bool isQpa)
        {
            switch (statusCode)
            {
                case bdgas_requestintake_statuscode.InQueue:
                    return "In Queue";
                case bdgas_requestintake_statuscode.ConceptApproval:
                    return "Concept Approval";
                case bdgas_requestintake_statuscode.SolutionDesign:
                    return isQpa ? "PROD" : "Solution Design";
                case bdgas_requestintake_statuscode.DEV:
                    return "DEV";
                case bdgas_requestintake_statuscode.QA:
                    return "QA";
                case bdgas_requestintake_statuscode.ReadyforSprintProdMove:
                    return isQpa ? "PROD" : "Ready For Sprint Prod Move";
                case bdgas_requestintake_statuscode.PROD:
                    if (isHypercare) return "Hypercare";
                    return isQpa ? "PROD" : "Prod";
                default:
                    return isQpa ? "PROD" : "Prod";
            }
        }

        private static string BuildTraversedPath(ProcessStage[] stages, ProcessStage selectedStage)
        {
            var stagesById = stages.ToDictionary(stage => stage.Id);
            var path = new List<Guid>();
            var currentStage = selectedStage;

            while (currentStage != null && currentStage.Id != Guid.Empty && !path.Contains(currentStage.Id))
            {
                path.Insert(0, currentStage.Id);

                var parentId = currentStage.ParentProcessStageId == null ? (Guid?)null : currentStage.ParentProcessStageId.Id;
                currentStage = parentId.HasValue && stagesById.ContainsKey(parentId.Value)
                    ? stagesById[parentId.Value]
                    : null;
            }

            var firstStage = stages.FirstOrDefault(stage => stage.StageName == "In Queue");
            if (firstStage != null)
            {
                path.Remove(firstStage.Id);
                path.Insert(0, firstStage.Id);
            }

            return string.Join(",", path);
        }

        private static Guid CreateWithoutCustomPlugins(IOrganizationService service, Entity entity, string description)
        {
            var request = new CreateRequest
            {
                Target = entity,
                RequestId = Guid.NewGuid()
            };
            return ((CreateResponse)ExecuteWithoutCustomPlugins(service, request, entity, description)).id;
        }

        private static void UpdateWithoutCustomPlugins(IOrganizationService service, Entity entity, string description)
        {
            var request = new UpdateRequest
            {
                Target = entity,
                RequestId = Guid.NewGuid()
            };
            ExecuteWithoutCustomPlugins(service, request, entity, description);
        }

        private static OrganizationResponse ExecuteWithoutCustomPlugins(IOrganizationService service, OrganizationRequest request, Entity entity, string description)
        {
            request.Parameters["BypassCustomPluginExecution"] = true;
            request.Parameters["SuppressCallbackRegistrationExpanderJob"] = true;

            try
            {
                var response = service.Execute(request);
                var createResponse = response as CreateResponse;
                if (request is CreateRequest && (createResponse == null || createResponse.id == Guid.Empty))
                {
                    throw new InvalidOperationException("Dataverse create did not return a non-empty record ID. Dependent writes will not be attempted.");
                }
                return response;
            }
            catch (Exception ex)
            {
                lock (ErrorLogLock)
                {
                    LogDataverseError(request.RequestName + " failed for " + description + ". RequestId=" + request.RequestId
                        + "; BypassCustomPluginExecution=true; SuppressCallbackRegistrationExpanderJob=true. | " + DescribeEntityForLog(entity));
                    LogDataverseException(ex);
                }

                throw;
            }
        }

        private static string DescribeEntityForLog(Entity entity)
        {
            var values = entity.Attributes.OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
                .Select(kvp => kvp.Key + "=" + FormatLogValue(kvp.Value));
            return entity.LogicalName + " Id=" + entity.Id + " { " + string.Join(", ", values) + " }";
        }

        private static string FormatLogValue(object value)
        {
            if (value == null)
            {
                return "<null>";
            }

            var money = value as Money;
            if (money != null)
            {
                return "Money(" + money.Value.ToString(CultureInfo.InvariantCulture) + ")";
            }

            var reference = value as EntityReference;
            if (reference != null)
            {
                var keys = string.Join(", ", reference.KeyAttributes.Select(kvp => kvp.Key + "=" + FormatLogValue(kvp.Value)));
                return "EntityReference(LogicalName=" + reference.LogicalName + ", Id=" + reference.Id
                    + ", Name=" + reference.Name + ", Keys={" + keys + "})";
            }

            var option = value as OptionSetValue;
            if (option != null)
            {
                return "OptionSetValue(" + option.Value.ToString(CultureInfo.InvariantCulture) + ")";
            }

            var options = value as OptionSetValueCollection;
            if (options != null)
            {
                return "OptionSetValueCollection(" + string.Join(", ", options.Select(item => item.Value.ToString(CultureInfo.InvariantCulture))) + ")";
            }

            var alias = value as AliasedValue;
            if (alias != null)
            {
                return "AliasedValue(" + alias.EntityLogicalName + "." + alias.AttributeLogicalName + "=" + FormatLogValue(alias.Value) + ")";
            }

            if (value is DateTime)
            {
                var date = (DateTime)value;
                return "System.DateTime(" + date.ToString("o", CultureInfo.InvariantCulture) + ", Kind=" + date.Kind + ")";
            }

            return value.GetType().FullName + "(" + Convert.ToString(value, CultureInfo.InvariantCulture) + ")";
        }

        private static void LogDataverseException(Exception exception)
        {
            LogDataverseError(exception.ToString());
            for (var current = exception; current != null; current = current.InnerException)
            {
                var faultException = current as FaultException<OrganizationServiceFault>;
                if (faultException == null) continue;

                var depth = 0;
                for (var fault = faultException.Detail; fault != null; fault = fault.InnerFault)
                {
                    LogDataverseError("Dataverse fault depth=" + depth + "; ActivityId=" + fault.ActivityId
                        + "; ErrorCode=0x" + fault.ErrorCode.ToString("X8", CultureInfo.InvariantCulture)
                        + "; Timestamp=" + fault.Timestamp.ToString("o", CultureInfo.InvariantCulture)
                        + "; Message=" + fault.Message);
                    foreach (var detail in fault.ErrorDetails)
                    {
                        LogDataverseError("ErrorDetails[" + detail.Key + "]=" + FormatLogValue(detail.Value));
                    }
                    if (!string.IsNullOrEmpty(fault.TraceText))
                    {
                        LogDataverseError("TraceText: " + fault.TraceText);
                    }
                    depth++;
                }
            }
        }

        private static void LogDataverseError(string message)
        {
            lock (ErrorLogLock)
            {
                Console.Error.WriteLine(message);
                try
                {
                    File.AppendAllText(ErrorLogFileName, DateTime.Now.ToString("o", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                {
                    Console.Error.WriteLine("Could not append migration diagnostics to " + Path.GetFullPath(ErrorLogFileName) + ": " + ex.Message);
                }
            }
        }

        private static bdgas_requestintake FindRequestIntakeByTicket(IOrganizationService service, string ticket)
        {
            var query = new QueryExpression(bdgas_requestintake.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(false)
            };
            query.Criteria.AddCondition("bdgas_requestintake", ConditionOperator.Equal, ticket);

            var entity = service.RetrieveMultiple(query).Entities.FirstOrDefault();
            return entity == null ? null : entity.ToEntity<bdgas_requestintake>();
        }

        private static EntityReference FindSystemUserLookup(IOrganizationService service, string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return null;
            }

            var cleaned = NormalizeLookupValue(rawValue);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return null;
            }

            var query = new QueryExpression(SystemUser.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(SystemUser.Fields.SystemUserId, SystemUser.Fields.FullName, SystemUser.Fields.InternalEMailAddress),
                TopCount = 1
            };
            query.Criteria.AddCondition(SystemUser.Fields.FullName, ConditionOperator.Like, "%" + cleaned + "%");

            var account = service.RetrieveMultiple(query).Entities.FirstOrDefault();
            return account == null ? null : new EntityReference(SystemUser.EntityLogicalName, account.Id);
        }

        private static EntityReference FindVendorLookup(IOrganizationService service, string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return null;
            }

            var cleaned = NormalizeLookupValue(rawValue);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return null;
            }

            var query = new QueryExpression(bdgas_vendor.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_vendor.Fields.bdgas_vendorId, bdgas_vendor.Fields.bdgas_Name),
                TopCount = 1
            };
            query.Criteria.AddCondition(bdgas_vendor.Fields.bdgas_Name, ConditionOperator.Like, "%" + cleaned + "%");

            var entity = service.RetrieveMultiple(query).Entities.FirstOrDefault();
            return entity == null ? null : new EntityReference(bdgas_vendor.EntityLogicalName, entity.Id);
        }

        private static EntityReference GetReferenceForCategoryAndName(IOrganizationService service, string entityLogicalName, string nameField, string categoryField, string category, string value)
        {
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var query = new QueryExpression(entityLogicalName)
            {
                ColumnSet = new ColumnSet(nameField, categoryField),
                TopCount = 1
            };
            query.Criteria.AddCondition(categoryField, ConditionOperator.Equal, category.Trim());
            query.Criteria.AddCondition(nameField, ConditionOperator.Equal, value.Trim());

            var entity = service.RetrieveMultiple(query).Entities.FirstOrDefault();
            return entity == null ? null : new EntityReference(entityLogicalName, entity.Id);
        }

        private static string GetValue(Dictionary<string, string> row, string key)
        {
            if (row == null || !row.TryGetValue(key, out var value))
            {
                return string.Empty;
            }

            return (value ?? string.Empty).Trim();
        }

        private static string GetString(Dictionary<string, string> row, string key)
        {
            return GetValue(row, key).Trim();
        }

        private static int GetRowNumber(Dictionary<string, string> row)
        {
            int rowNumber;
            return int.TryParse(GetValue(row, SourceRowNumberKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out rowNumber)
                ? rowNumber
                : 0;
        }

        private static string NormalizeLookupValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            if (trimmed.StartsWith("\"") && trimmed.EndsWith("\""))
            {
                trimmed = trimmed.Trim('"');
            }

            if (trimmed.Contains(";#"))
            {
                var split = trimmed.Split(new[] { ";#" }, 2, StringSplitOptions.None);
                trimmed = split[0].Trim();
            }

            return Regex.Replace(trimmed, @"\s+", " ").Trim();
        }

        private static IEnumerable<string> SplitCsvList(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                yield break;
            }

            foreach (var part in value.Split(';'))
            {
                var item = part.Trim();
                if (string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                if (item.StartsWith("#"))
                {
                    item = item.Substring(1).Trim();
                }

                if (item.StartsWith("\"") && item.EndsWith("\""))
                {
                    item = item.Trim('"');
                }

                yield return item;
            }
        }

        private static string GetFirstDeveloperName(Dictionary<string, string> row)
        {
            return SplitCsvList(GetValue(row, "Developer(s)")).FirstOrDefault() ?? string.Empty;
        }

        private static string GetFirstBotInformedName(Dictionary<string, string> row)
        {
            return SplitCsvList(GetValue(row, "Bot Informed")).FirstOrDefault() ?? string.Empty;
        }

        private static bool? ParseNullableBool(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            if (bool.TryParse(trimmed, out var boolValue))
            {
                return boolValue;
            }

            var lower = trimmed.ToLowerInvariant();
            if (lower == "true" || lower == "yes" || lower == "y" || lower == "1")
            {
                return true;
            }

            if (lower == "false" || lower == "no" || lower == "n" || lower == "0")
            {
                return false;
            }

            return null;
        }

        private static bool ParseBool(string value)
        {
            return ParseNullableBool(value) ?? false;
        }

        private static DateTime? ParseNullableDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var candidate = value.Trim();
            if (string.Equals(candidate, "189912", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (Regex.IsMatch(candidate, @"(?:Z|[+-]\d{2}:?\d{2})$", RegexOptions.IgnoreCase)
                && DateTimeOffset.TryParse(candidate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedOffset))
            {
                return parsedOffset.UtcDateTime;
            }

            if (DateTime.TryParse(candidate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
            }

            if (DateTime.TryParse(candidate, new CultureInfo("en-US"), DateTimeStyles.None, out var parsedUs))
            {
                return DateTime.SpecifyKind(parsedUs, DateTimeKind.Unspecified);
            }

            return null;
        }

        private static DateTime? ParseCsvDate(
            string value,
            MigrationMetadataCache metadata,
            string entityName,
            string attributeName)
        {
            var parsed = ParseNullableDate(value);
            return parsed.HasValue
                ? ConvertDateForBehavior(parsed.Value, metadata, entityName, attributeName)
                : (DateTime?)null;
        }

        private static DateTime ConvertDateForBehavior(
            DateTime value,
            MigrationMetadataCache metadata,
            string entityName,
            string attributeName)
        {
            var behavior = metadata.GetDateTimeBehavior(entityName, attributeName);
            if (behavior == DateTimeBehavior.UserLocal)
            {
                return value.Kind == DateTimeKind.Utc ? value : CsvTimeZone.ConvertToUTC(value);
            }

            if (behavior == DateTimeBehavior.DateOnly)
            {
                var dateOnlyValue = value.Kind == DateTimeKind.Utc
                    ? TimeZoneInfo.ConvertTimeFromUtc(value, CsvTimeZone)
                    : value;
                return DateTime.SpecifyKind(dateOnlyValue.Date, DateTimeKind.Unspecified);
            }

            if (behavior == DateTimeBehavior.TimeZoneIndependent)
            {
                var localValue = value.Kind == DateTimeKind.Utc
                    ? TimeZoneInfo.ConvertTimeFromUtc(value, CsvTimeZone)
                    : value;
                return DateTime.SpecifyKind(localValue, DateTimeKind.Unspecified);
            }

            throw new InvalidOperationException(
                "Unsupported date/time behavior '" + behavior + "' for " + entityName + "." + attributeName + ".");
        }

        private static decimal? ParseNullableDecimal(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var candidate = value.Trim();
            candidate = candidate.Replace("$", string.Empty);
            candidate = candidate.Replace(",", string.Empty);
            candidate = candidate.Replace("(", "-");
            candidate = candidate.Replace(")", string.Empty);
            candidate = candidate.Replace("%", string.Empty);
            candidate = candidate.Trim();

            decimal result;
            if (decimal.TryParse(candidate, NumberStyles.Any, CultureInfo.InvariantCulture, out result))
            {
                return result;
            }

            return null;
        }

        private static int? ParseNullableInt(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var candidate = value.Trim();
            int result;
            if (int.TryParse(candidate, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
            {
                return result;
            }

            var decimalValue = ParseNullableDecimal(candidate);
            if (decimalValue.HasValue)
            {
                return (int)Math.Round(decimalValue.Value, MidpointRounding.AwayFromZero);
            }

            return null;
        }

        private static bdgas_requestintake_statuscode? MapLifecyclePhaseToStatusCode(string value)
        {
            var normalized = NormalizeEnumValue(value);
            var map = new Dictionary<string, bdgas_requestintake_statuscode>(StringComparer.OrdinalIgnoreCase)
            {
                { "in queue", bdgas_requestintake_statuscode.InQueue },
                { "prod on hold", bdgas_requestintake_statuscode.PRODonHold },
                { "concept approval", bdgas_requestintake_statuscode.ConceptApproval },
                { "solution design", bdgas_requestintake_statuscode.SolutionDesign },
                { "dev", bdgas_requestintake_statuscode.DEV },
                { "qa", bdgas_requestintake_statuscode.QA },
                { "ready for sprint/prod move", bdgas_requestintake_statuscode.ReadyforSprintProdMove },
                { "prod", bdgas_requestintake_statuscode.PROD },
                { "retired and retained", bdgas_requestintake_statuscode.RetiredandRetained },
                { "cancelled", bdgas_requestintake_statuscode.Cancelled },
                { "on hold", bdgas_requestintake_statuscode.OnHold }
            };

            return map.ContainsKey(normalized) ? map[normalized] : bdgas_requestintake_statuscode.InQueue;
        }

        private static bdgas_requestintake_statecode MapStatusCodeToStateCode(bdgas_requestintake_statuscode? statusCode, bool botIsActive)
        {
            return bdgas_requestintake_statecode.Active;
        }

        private static bool IsInactiveStatus(bdgas_requestintake_statuscode? statusCode)
        {
            return statusCode == bdgas_requestintake_statuscode.Cancelled || statusCode == bdgas_requestintake_statuscode.RetiredandRetained;
        }

        private static bdgas_requestintake_bdgas_botbusinesscriticality? MapBotBusinessCriticality(string value)
        {
            var normalized = NormalizeEnumValue(value);
            var map = new Dictionary<string, bdgas_requestintake_bdgas_botbusinesscriticality>(StringComparer.OrdinalIgnoreCase)
            {
                { "mission critical", bdgas_requestintake_bdgas_botbusinesscriticality.MissionCritical },
                { "high important", bdgas_requestintake_bdgas_botbusinesscriticality.HighImportant },
                { "important", bdgas_requestintake_bdgas_botbusinesscriticality.Important },
                { "normal", bdgas_requestintake_bdgas_botbusinesscriticality.Normal },
                { "4 - normal", bdgas_requestintake_bdgas_botbusinesscriticality.Normal },
                { "3 - important", bdgas_requestintake_bdgas_botbusinesscriticality.Important },
                { "2 - high important", bdgas_requestintake_bdgas_botbusinesscriticality.HighImportant },
                { "1 - mission critical", bdgas_requestintake_bdgas_botbusinesscriticality.MissionCritical }
            };

            return map.ContainsKey(normalized) ? map[normalized] : (bdgas_requestintake_bdgas_botbusinesscriticality?)null;
        }

        private static bdgas_requestintake_bdgas_l1option? MapL1Option(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = NormalizeEnumValue(value);
            var map = new Dictionary<string, bdgas_requestintake_bdgas_l1option>(StringComparer.OrdinalIgnoreCase)
            {
                { "hire to retire (htr)", bdgas_requestintake_bdgas_l1option.HiretoRetire_HTR },
                { "it strategy to operations (it-sto)", bdgas_requestintake_bdgas_l1option.ITStrategytoOperations_ITSTO },
                { "market to opportunity (mto)", bdgas_requestintake_bdgas_l1option.MarkettoOpportunity_MTO },
                { "plan to deliver (ptd)", bdgas_requestintake_bdgas_l1option.PlantoDeliver_PTD },
                { "plan to forecast (ptf)", bdgas_requestintake_bdgas_l1option.PlantoForecast_PTF },
                { "product lifecycle management (plm)", bdgas_requestintake_bdgas_l1option.ProductLifecycleManagement_PLM },
                { "quote to cash (qtc)", bdgas_requestintake_bdgas_l1option.QuotetoCash_QTC },
                { "record to report (rtr)", bdgas_requestintake_bdgas_l1option.RecordtoReport_RTR },
                { "source to pay (stp)", bdgas_requestintake_bdgas_l1option.SourcetoPay_STP },
                { "master data management (mdm)", bdgas_requestintake_bdgas_l1option.MasterDataManagement_MDM }
            };

            foreach (var kvp in map)
            {
                if (normalized.Contains(kvp.Key) || kvp.Key.Contains(normalized))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        private static string NormalizeEnumValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return Regex.Replace(value.Trim(), @"\s+", " ").ToLowerInvariant();
        }
    }
}
