﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Script.Serialization;
using InsuranceServices.Models;
using Newtonsoft.Json;
using EasyDox;

namespace InsuranceServices.Controllers
{
    public class ConditionsController : Controller
    {
        InsuranceServicesContext db = new InsuranceServicesContext();

        enum ResponseType
        {
            Bad,
            Critical,
            Good
        }

        class ResponseToClient
        {
            public ResponseType responseType { get; set; }
            public string responseText { get; set; }
        }

        public string GetRegions()
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            List<string> regioneOfRegistration = db.RegioneOfRegistration.Select(region => region.Name).ToList();
            regioneOfRegistration.Sort();
            return js.Serialize(regioneOfRegistration);
        }

        public string GetCities(string regionName)
        {
            JavaScriptSerializer js = new JavaScriptSerializer();

            int idRegioneOfRegistration = db.RegioneOfRegistration.Where(r => r.Name == regionName).Select(r => r.Id).First();

            List<string> cityOfRegistration = db.CityOfRegistration
                                                .Where(c => c.IdRegioneOfRegistration == idRegioneOfRegistration)
                                                .Select(c => c.Name)
                                                .ToList();
            cityOfRegistration.Sort();
            return js.Serialize(cityOfRegistration);
        }

        public string GetCodesTSC()
        {
            JavaScriptSerializer js = new JavaScriptSerializer();

            var TSC = db.TSC
                        .OrderBy(tsc => tsc.Code)
                        .ToList();
            List<TSCToSend> TSCToSends = new List<TSCToSend>();

            foreach (var tsc in TSC)
            {
                TSCToSend currentTSCToSend = new TSCToSend();
                currentTSCToSend.codeNum = tsc.Code;
                currentTSCToSend.cityName = tsc.CityOfRegistration.Name;
                currentTSCToSend.regionName = tsc.CityOfRegistration.RegioneOfRegistration.Name;

                TSCToSends.Add(currentTSCToSend);
            }
 
            return js.Serialize(TSCToSends);
        }

        public string GetCountries()
        {
            JavaScriptSerializer js = new JavaScriptSerializer();

            List<string> countries = db.CountryOfRegistration.Select(c => c.Name).ToList();
            countries.Sort();

            return js.Serialize(countries);
        }

        [HttpPost]
        public string SendConditions()
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            ResponseToClient responseToClient = new ResponseToClient();
            List<ResponseToClient> warnings = new List<ResponseToClient>();

            string defaultErrorMessage = "Будь ласка скористайтеся сервісом пізніше. Дякуємо за розуміння.";
            string defaultConfigMessage = "За замовчанням встановлено";

            dynamic dataParsed = GetPotsRequestBody();
            if(dataParsed == null)
            {
                responseToClient.responseType = ResponseType.Critical;
                responseToClient.responseText = "Виникла помилка з обробкою Ваших даних. " + defaultErrorMessage;
                return js.Serialize(responseToClient);
            }           

            ConditionsForDocument conditionsForDocument = new ConditionsForDocument();

            if(dataParsed.trans.type == null || dataParsed.trans.extra == null)
            {
                responseToClient.responseType = ResponseType.Critical;
                responseToClient.responseText = "Виникла помилка при обробці даних. " + defaultErrorMessage;
                return js.Serialize(responseToClient);
            }
            string currentTransportType = dataParsed.trans.type;
            string currentInsurenceType = dataParsed.trans.extra;

            //var a = db.CarGlobalType.Where(c => c.Name == currentTransportType).First();
            //var b = db.CarInsuranceType.Where(c => c.Type == currentInsurenceType).First();
            Transport transport = new Transport();
            CarGlobalType currentType = new CarGlobalType();
            CarInsuranceType currentSubType = new CarInsuranceType();
            currentType = db.CarGlobalType.Where(c => c.Name == currentTransportType).First();
            currentSubType = db.CarInsuranceType.Where(c => c.Type == currentInsurenceType).First();

            transport.Type = currentType;
            transport.SubType = currentSubType;

            conditionsForDocument.Transport = transport;
            
            if(conditionsForDocument.Transport.Type == null || conditionsForDocument.Transport.SubType == null)
            {
                responseToClient.responseType = ResponseType.Critical;
                responseToClient.responseText = "Не корректні дані для типу автомобіля.";
                return js.Serialize(responseToClient);
            }

            //PlaceOfRegistration placeOfRegistration = new PlaceOfRegistration();
            //CityOfRegistration cityOfRegistration = new CityOfRegistration();
            //CountryOfRegistration countryOfRegistration = new CountryOfRegistration();

            //GetPlaceOfRegistration(dataParsed, ref cityOfRegistration, ref countryOfRegistration);

            //placeOfRegistration.CityOfRegistration = cityOfRegistration;
            //placeOfRegistration.CountryOfRegistration = countryOfRegistration;

            //conditionsForDocument.PlaceOfRegistration = placeOfRegistration;

            conditionsForDocument.InsuranceZoneOfRegistration = GetZoneOfRegistration(dataParsed);
            
            if(conditionsForDocument.InsuranceZoneOfRegistration == null)
            {
                responseToClient.responseType = ResponseType.Critical;
                responseToClient.responseText = "Виникла проблема з місцем реєстрації Вашого авто. Будь ласка поверніться на попередню сторінку та змініть місце реєстрації автомобіля";
                return js.Serialize(responseToClient);
            }

            bool isLegalEntity;
            bool parseIsLegalEntityResult = bool.TryParse(dataParsed.isLegalEntity.ToString(), out isLegalEntity);
            if (!parseIsLegalEntityResult)
            {
                isLegalEntity = false;
                responseToClient.responseType = ResponseType.Bad;
                responseToClient.responseText = "Не вдалося отримати дані по юридичній або фізичнії особі. " + defaultConfigMessage + " \"Фізична особа\".";
                warnings.Add(responseToClient);
            }
            conditionsForDocument.IsLegalEntity = isLegalEntity;

            PeriodOfDocument periodOfDocument = new PeriodOfDocument();
            double currentPeriod;
            bool parsePeriodResult = double.TryParse(dataParsed.period.term.ToString(), out currentPeriod);
            if (!parsePeriodResult)
            {
                currentPeriod = 12.0;
                responseToClient.responseType = ResponseType.Bad;
                responseToClient.responseText = "Не вдалося отримати дані по періоду страхування. " + defaultConfigMessage + " \"12 місяців\".";
                warnings.Add(responseToClient);
            }
            periodOfDocument.Period = currentPeriod;

            bool isMTC;
            bool parseIsMTCResult = bool.TryParse(dataParsed.period.otk.ToString(), out isMTC);
            if (!parseIsMTCResult)
            {
                isMTC = false;
                responseToClient.responseType = ResponseType.Bad;
                responseToClient.responseText = "Не вдалося отримати дані про необхідності проходження обов'язкового технічного контролю. " + defaultConfigMessage + " \"Не проходить\".";
                warnings.Add(responseToClient);
            }
            periodOfDocument.isMTC = isMTC;
            conditionsForDocument.PeriodOfDocument = periodOfDocument;

            bool isUseAsTaxi;
            bool parseIsTaxiCarResult = bool.TryParse(dataParsed.taxi.ToString(), out isUseAsTaxi);
            if (!parseIsTaxiCarResult)
            {
                isUseAsTaxi = false;
                responseToClient.responseType = ResponseType.Bad;
                responseToClient.responseText = "Не вдалося з'ясувати чи використовується Ваше авто в таксі. " + defaultConfigMessage + " \"Не використовується\".";
                warnings.Add(responseToClient);
            }
            conditionsForDocument.IsUseAsTaxi = isUseAsTaxi;


            DriverAccident driverAccident = new DriverAccident();
            bool isDriverWasInAccident;
            bool parseIsDriverWasInAccident = bool.TryParse(dataParsed.dtp.dtpState.ToString(), out isDriverWasInAccident);
            if (!parseIsDriverWasInAccident)
            {
                isDriverWasInAccident = false;
                responseToClient.responseType = ResponseType.Bad;
                responseToClient.responseText = "Не вдалося отримати дані про наявність страхових випадків. " + defaultConfigMessage + " \"Не було\".";
                warnings.Add(responseToClient);
            }
            if (isDriverWasInAccident)
            {
                int currentLastAccidentYear;
                bool parseLastAccidentYear = Int32.TryParse(dataParsed.dtp.dtpTerm.ToString(), out currentLastAccidentYear);
                if (!parseLastAccidentYear)
                {
                    currentLastAccidentYear = 4;
                    responseToClient.responseType = ResponseType.Bad;
                    responseToClient.responseText = "Не вдалося отримати дані про час останнього страхового випадку. " + defaultConfigMessage + " \"Не було\".";
                    warnings.Add(responseToClient);
                }
                //for 4 year and more, coef is same
                driverAccident.IsDriverWasInAccident = isDriverWasInAccident;
                driverAccident.LastAccidentYear = currentLastAccidentYear;
            }
            else
            {
                driverAccident.IsDriverWasInAccident = isDriverWasInAccident;
                driverAccident.LastAccidentYear = 4;
            }

            conditionsForDocument.DriverAccident = driverAccident;

            bool isClientHasPrivilegs;
            bool parseIsClientHasPrivilegs = bool.TryParse(dataParsed.privilege.ToString(), out isClientHasPrivilegs);
            if (!parseIsClientHasPrivilegs)
            {
                isClientHasPrivilegs = false;
                responseToClient.responseType = ResponseType.Bad;
                responseToClient.responseText = "Не вдалося отримати дані про Ваші пільги. " + defaultConfigMessage + " \"Відсутні\".";
                warnings.Add(responseToClient);
            }
            conditionsForDocument.IsClientHasPrivilegs = isDriverWasInAccident;

            int documentAdditionalLimits;
            bool parseDocumentAdditionalLimits = int.TryParse(dataParsed.osagoLimits.ToString(), out documentAdditionalLimits);
            if (!parseDocumentAdditionalLimits)
            {
                documentAdditionalLimits = 0;
                responseToClient.responseType = ResponseType.Bad;
                responseToClient.responseText = "Не вдалося отримати дані про Ваше бажання збільшити ліміти відповідальності. " + defaultConfigMessage + " \"Не збільшувати\".";
                warnings.Add(responseToClient);
            }
            conditionsForDocument.DocumentAdditionalLimits = documentAdditionalLimits;

            //Test Data
            List<CompanyToSend> companiesToSend = new List<CompanyToSend>();

            var companies = db.Company.Select(c => c.Name).ToList();
            foreach(var c in companies)
            {
                CompanyToSend company= new CompanyToSend();
                company.CompanyName = c;

                companiesToSend.Add(company);
            }

            if(warnings.Count > 0)
            {
                DataWithWarningToSend dataWithWarningToSend = new DataWithWarningToSend();
                dataWithWarningToSend.CompaniesToSend = companiesToSend;
                dataWithWarningToSend.ErrorMessagesToClients = warnings;

                return js.Serialize(dataWithWarningToSend);
            }
            //Test Data End
            var test = GetCompaniesForConditions(conditionsForDocument);
            return js.Serialize(test);
            return js.Serialize(companiesToSend);
        }

        //private List<CompanyToSend> GetCompaniesForConditions(ConditionsForDocument conditionsForDocument)
        private List<CompanyToSend> GetCompaniesForConditions(ConditionsForDocument conditionsForDocument)
        {
            List<CompanyToSend> companyToSends = new List<CompanyToSend>();

            List<Company> companies = db.Company.ToList();
            double baseCoef = 180.0;
            //Dictionary<string, List<double>> companiesM = new Dictionary<string, List<double>>();
            foreach (var company in companies)
            {
                List<int> companyMiddlemenId = db.CompanyMiddleman.Where(cm => cm.Company.Name == company.Name).Select(cm => cm.Id).ToList();

                double K1Value, K2Value, K3Value, K4Value, K5Value, K6Value, K7Value, BMValue, KParkValue, KPilgValue;
                
                foreach (var middlemanId in companyMiddlemenId)
                {
                    //CompanyToSend companyToSend = new CompanyToSend();

                    List<Franchise> franchises = new List<Franchise>();
                    try
                    {
                        franchises = db.ContractFranchise.Where(cf => cf.CompanyContractTypes.ContractType.Name == "ГО"
                                                                       && cf.CompanyContractTypes.CompanyMiddleman.Id == middlemanId).Select(cf => cf.Franchise).ToList();
                    }
                    catch
                    {
                        continue;
                    }


                    try
                    {
                        K1Value = db.K1.Where(k => k.CompanyMiddleman.Id == middlemanId
                                           && k.CarInsuranceType.Type == conditionsForDocument.Transport.SubType.Type)
                                           .Select(k => k.Value).First();
                        if (K1Value == 0)
                            continue;
                    }
                    catch
                    {
                        continue;
                    }

                    try
                    {
                        K3Value = db.K3.Where(k => k.CompanyMiddleman.Id == middlemanId
                                            && k.IdInsuranceZoneOfReg == conditionsForDocument.InsuranceZoneOfRegistration.Id
                                            && k.IsLegalEntity == conditionsForDocument.IsLegalEntity
                                            && k.CarInsuranceType.Type == conditionsForDocument.Transport.SubType.Type)
                                            .Select(k => k.Value).First();
                        if (K3Value == 0)
                            continue;
                    }
                    catch
                    {
                        continue;
                    }

                    try
                    {
                        if (conditionsForDocument.InsuranceZoneOfRegistration.Name == "Зона 7" || conditionsForDocument.PeriodOfDocument.isMTC)
                            K5Value = 1.0;
                        else
                            K5Value = db.K5.Where(k => k.Period == conditionsForDocument.PeriodOfDocument.Period).Select(k => k.Value).First();

                        if (K5Value == 0)
                            continue;
                    }
                    catch
                    {
                        continue;
                    }

                    try
                    {
                        K6Value = (double)db.K6.Where(k => k.IsCheater == false).Select(k => k.Value).First();

                        if (K6Value == 0)
                            K6Value = 1.0;
                    }
                    catch
                    {
                        K6Value = 1.0;
                    }

                    try
                    {
                        if (K6Value != 1.0)
                            K7Value = db.K7.Where(k => k.Period == conditionsForDocument.PeriodOfDocument.Period).Select(k => k.Value).First();
                        else
                            K7Value = 1.0;
                    }
                    catch
                    {
                        continue;
                    }

                    try
                    {
                        //in future will be add field in conditionsForDocument and it will be same "TransportCount"
                        int transportCount = 1;
                        //
                        if (!conditionsForDocument.IsLegalEntity)
                            KParkValue = 1.0;
                        else
                            KParkValue = db.DiscountByQuantity.Where(d => transportCount >= d.TransportCountFrom
                                                                  && transportCount <= d.TransportCountTo)
                                                                  .Select(d => d.Value).First();
                    }
                    catch
                    {
                        continue;
                    }

                    try
                    {
                        if (!conditionsForDocument.IsClientHasPrivilegs)
                            KPilgValue = 1.0;
                        else
                            KPilgValue = 0.5;
                    }
                    catch
                    {
                        continue;
                    }

                    

                    foreach (var f in franchises)
                    {
                        CompanyToSend companyToSend = new CompanyToSend();
                        try
                        {
                            K2Value = db.K2.Where(k => k.CompanyMiddleman.Id == middlemanId
                                                && k.IdInsuranceZoneOfReg == conditionsForDocument.InsuranceZoneOfRegistration.Id
                                                && k.IsLegalEntity == conditionsForDocument.IsLegalEntity
                                                && k.CarInsuranceType.Type == conditionsForDocument.Transport.SubType.Type
                                                && k.ContractFranchise.Franchise.Sum == f.Sum)
                                                .Select(k => k.Value).First();

                            if (K2Value == 0)
                                continue;
                        }
                        catch
                        {
                            continue;
                        }                        

                        try
                        {
                            K4Value = db.K4.Where(k => k.CompanyMiddleman.Id == middlemanId
                                            && k.IdInsuranceZoneOfReg == conditionsForDocument.InsuranceZoneOfRegistration.Id
                                            && k.ContractFranchise.Franchise.Sum == f.Sum
                                            && k.IsLegalEntity == conditionsForDocument.IsLegalEntity)
                                            .Select(k => k.Value).First();

                            if (K4Value == 0)
                                continue;
                        }
                        catch
                        {
                            continue;
                        }                        

                        try
                        {
                            BMValue = db.BonusMalus.Where(bm => bm.IdCompanyMiddleman == middlemanId
                                                        && bm.IdInsuranceZoneOfReg == conditionsForDocument.InsuranceZoneOfRegistration.Id
                                                        && bm.ContractFranchise.Franchise.Sum == f.Sum
                                                        && bm.IsLegalEntity == conditionsForDocument.IsLegalEntity
                                                        && bm.CarInsuranceType.Type == conditionsForDocument.Transport.SubType.Type)
                                                        .Select(bm => bm.Value).First();
                            if (BMValue == 0)
                                continue;
                        }
                        catch
                        {
                            continue;
                        }                        

                        //CompanyDetail companyDetail = new CompanyDetail();
                        //try
                        //{
                        //    companyDetail = db.CompanyDetail.Where(cd => cd.IdCompany == company.Id).First();
                        //}
                        //catch
                        //{
                        //    continue;
                        //}

                        companyToSend.CompanyName = company.Name;
                        companyToSend.IdCompanyMiddleman = middlemanId;
                        companyToSend.CompanyRate = 90;//companyDetail.SummaryRait;

                        //var companyFeaturesToCompany = db.CompanyFeatureToCompany.Where(cftc => cftc.IdCompany == company.Id).ToList();
                        //List<CompanyFeature> features = new List<CompanyFeature>();
                        //foreach(var cftc in companyFeaturesToCompany)
                        //{
                        //    CompanyFeature companyFeature = new CompanyFeature();
                        //    companyFeature.Title = cftc.CompanyFeature.Name;
                        //    companyFeature.IconPath = cftc.CompanyFeature.Icon;

                        //    features.Add(companyFeature);
                        //}
                        companyToSend.CompanyFeatures = null;//features;

                        companyToSend.Franchise = f.Sum;
                        double tempPrice = baseCoef * K1Value * K2Value * K3Value * K4Value * K5Value * K6Value * K7Value * BMValue * KParkValue * KPilgValue;
                        companyToSend.FullPrice = Math.Round(tempPrice, 0);
                        //temp row, in resent feature need to add DiscountForClient in DB
                        companyToSend.DiscountPrice = Math.Ceiling(companyToSend.FullPrice * 0.9);

                        //temp row, in feature need to add in ImageType field "Type" {1, 2, 3 . . .} and field "Name" will be {small, middle, large . . . } 
                        //CompanyIMG companyIMG = db.CompanyIMG.Where(ci => ci.IdCompany == company.Id && ci.ImageType.Name == "1").First();
                        //companyToSend.ImgPath = companyIMG.Path;
                        //companyToSend.ImageSize = companyIMG.ImageType.Name;
                        //companyToSend.PageCompanyInfoPath = companyIMG.ReferensToCompanyPage;

                        companyToSend.Coefs = new List<CoefToSend>
                        {
                            new CoefToSend { Name="baseCoef", Value=baseCoef},
                            new CoefToSend {Name="K1", Value=K1Value},
                            new CoefToSend {Name="K2", Value=K2Value},
                            new CoefToSend {Name="K3", Value=K3Value},
                            new CoefToSend {Name="K4", Value=K4Value},
                            new CoefToSend {Name="K5", Value=K5Value},
                            new CoefToSend {Name="K6", Value=K6Value},
                            new CoefToSend {Name="K7", Value=K7Value},
                            new CoefToSend {Name="BM", Value=BMValue},
                            new CoefToSend {Name="KPark", Value=KParkValue},
                            new CoefToSend {Name="KPilg", Value=KPilgValue}
                        };
                        //companyToSend.Coefs = new List<double>() { baseCoef, K1Value, K2Value, K3Value, K4Value, K5Value, K6Value, K7Value, BMValue, KParkValue, KPilgValue };
                        
                        companyToSends.Add(companyToSend);
                    }
                    if (franchises == null || franchises.Count == 0)
                        continue;
                }           
            }
            return companyToSends;
        }

        [HttpPost]
        public string CreateContracts()
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            ResponseToClient responseToClient = new ResponseToClient();


            string defaultErrorMessage = "Будь ласка скористайтеся сервісом пізніше. Дякуємо за розуміння.";

            dynamic dataParsed = GetPotsRequestBody();
            if(dataParsed == null)
            {
                responseToClient.responseType = ResponseType.Critical;
                responseToClient.responseText = "Виникла помилка з обробкою Ваших даних. " + defaultErrorMessage;
                return js.Serialize(responseToClient);
            }

            ContractForGeneration contractForGeneration = new ContractForGeneration();

            //waith while from front will be get this field
            int idCompanyMiddleman = Convert.ToInt32(dataParsed.idCompanyMiddleman);
            int companyCode = db.CompanyMiddleman.Where(cm => cm.Id == idCompanyMiddleman).Select(cm => cm.Company.Code).FirstOrDefault();
            contractForGeneration.CompanyCode = '0' + companyCode.ToString();
            DateTime dateFrom = Convert.ToDateTime(dataParsed.termFrom);
            DateTime dateTo = Convert.ToDateTime(dataParsed.termTo);
            double franchise = Convert.ToDouble(dataParsed.frans);
            contractForGeneration.TermFrom = dateFrom;
            contractForGeneration.TermTo = dateTo;
            contractForGeneration.Franchise = franchise;

            //Add checking if client exist
            Client client = new Client();
            client.IsLegalEntity = dataParsed.personalData.isLegalEntity;
            client.Phone = dataParsed.personalData.phone;
            client.Address = dataParsed.region + ",\n" + dataParsed.city + ",\n" + dataParsed.street + ", " + dataParsed.house + ", " + dataParsed.flat;
            db.Client.Add(client);
            db.SaveChanges();

            contractForGeneration.Phone = client.Phone;
            contractForGeneration.Address = client.Address;

            int idClient = db.Client.Where(c => c.Phone == client.Phone && c.Address == client.Address).Select(c => c.Id).FirstOrDefault();
            if (client.IsLegalEntity)
            {
                LegalEntityClient legalEntityClient = new LegalEntityClient();
                legalEntityClient.Name = dataParsed.personalData.companyName;
                legalEntityClient.EDRPOU = dataParsed.personalData.egrpou;
                legalEntityClient.IdClient = idClient;
                db.LegalEntityClient.Add(legalEntityClient);
                db.SaveChanges();
            }
            else
            {
                IndividualClient individualClient = new IndividualClient();
                individualClient.Name = dataParsed.personalData.name;
                individualClient.Surname = dataParsed.personalData.surname;
                try
                {
                    individualClient.FatherName = dataParsed.personalData.patronymic;
                }
                catch
                {
                    individualClient.FatherName = "";
                }
                individualClient.DateOfBirth = dataParsed.personalData.dateOfBirth;
                individualClient.PersonalCode = dataParsed.personalData.inn;
                individualClient.IdClient = idClient;
                db.IndividualClient.Add(individualClient);
                db.SaveChanges();
            }

            if (client.IsLegalEntity)
            {
                LegalEntityClient legalEntityClient = db.LegalEntityClient.Where(lec => lec.IdClient == idClient).FirstOrDefault();
                contractForGeneration.ClientSurname = legalEntityClient.Name;
                contractForGeneration.EDRPOU = legalEntityClient.EDRPOU;
            }
            else
            {
                IndividualClient individualClient = db.IndividualClient.Where(ic => ic.IdClient == idClient).FirstOrDefault();
                contractForGeneration.ClientName = individualClient.Name;
                contractForGeneration.ClientSurname = individualClient.Surname;
                contractForGeneration.ClientFathername = individualClient.FatherName;
                contractForGeneration.PersonalCode = individualClient.PersonalCode;
                contractForGeneration.DateOfBirth = individualClient.DateOfBirth;
            }

            contractForGeneration.DocumentType = dataParsed.documents.type;
            contractForGeneration.DocumentNumber = dataParsed.documents.number;
            contractForGeneration.DateOfIssued = dataParsed.documents.when;

            if(dataParsed.documents.type != "id-card")
            {
                contractForGeneration.DocumentSeria = dataParsed.documents.lot;
                contractForGeneration.IssuedBy = dataParsed.documents.issuedBy;
            }

            contractForGeneration.PlaceOfRegistration = GetPlaceOfReg(dataParsed);//trouble
            contractForGeneration.IsTaxi = dataParsed.taxi;
            contractForGeneration.IsOTK = dataParsed.otk;
            contractForGeneration.CarSubType = dataParsed.transExtra;
            contractForGeneration.RegistrationNumber = dataParsed.autoNumber;
            contractForGeneration.Mark = dataParsed.mark;
            contractForGeneration.Model = dataParsed.model;
            contractForGeneration.VinCode = dataParsed.chassis;
            contractForGeneration.Year = dataParsed.autoYear;

            //contractForGeneration.Coefs = dataParsed.coefs;

            contractForGeneration.DateOfPayment = DateTime.Now;
            contractForGeneration.DateOfTheContract = DateTime.Now;

            string finalDocument = GenerateContract(contractForGeneration);

            return js.Serialize(finalDocument);
        }

        private string GenerateContract(ContractForGeneration contractForGeneration)
        {
            var fieldValues = new Dictionary<string, string> {
                {"Код страховика", contractForGeneration.CompanyCode.ToString()},
                {"ГП", "00"},
                {"ХП", "00"},
                {"ДП", contractForGeneration.TermFrom.Day < 10 ? "0" + contractForGeneration.TermFrom.Day.ToString() : contractForGeneration.TermFrom.Day.ToString()},
                {"МП", contractForGeneration.TermFrom.Month < 10 ? "0" + contractForGeneration.TermFrom.Month.ToString() : contractForGeneration.TermFrom.Month.ToString()},
                {"РП", Convert.ToString(contractForGeneration.TermFrom.Year.ToString()[2]) + Convert.ToString(contractForGeneration.TermFrom.Year.ToString()[3])},
                {"ДЗ", contractForGeneration.TermTo.Day < 10 ? "0" + contractForGeneration.TermTo.Day.ToString() : contractForGeneration.TermTo.Day.ToString()},
                {"МЗ", contractForGeneration.TermTo.Month < 10 ? "0" + contractForGeneration.TermTo.Month.ToString() : contractForGeneration.TermTo.Month.ToString()},
                {"РЗ", Convert.ToString(contractForGeneration.TermTo.Year.ToString()[2]) + Convert.ToString(contractForGeneration.TermTo.Year.ToString()[3])},
                {"Розмір франшизи", contractForGeneration.Franchise.ToString() + " гривень"},
                {"Прізвище", contractForGeneration.ClientSurname.ToString()},//add field companyName
                {"Ім'я", contractForGeneration.ClientName.ToString()},
                {"По батькові", contractForGeneration.ClientFathername.ToString()},
                {"Код ЄДРПОУ", contractForGeneration.EDRPOU == null ? "" : contractForGeneration.EDRPOU.ToString()},
                {"Адреса", contractForGeneration.Address.ToString()},
                {"ДН", contractForGeneration.DateOfBirth == null ? "" : contractForGeneration.DateOfBirth.Day < 10 ? "0" + contractForGeneration.DateOfBirth.Day.ToString() : contractForGeneration.DateOfBirth.Day.ToString()},
                {"МН", contractForGeneration.DateOfBirth == null ? "" : contractForGeneration.DateOfBirth.Month < 10 ? "0" +  contractForGeneration.DateOfBirth.Month.ToString() : contractForGeneration.DateOfBirth.Month.ToString()},
                {"РН", contractForGeneration.DateOfBirth == null ? "" : contractForGeneration.DateOfBirth.Year.ToString()},
                {"Код ІНПП", contractForGeneration.PersonalCode == null ? "" : contractForGeneration.PersonalCode.ToString()},
                {"Тип документу", contractForGeneration.DocumentType == null ? "" : contractForGeneration.DocumentType.ToString()},
                {"Серія документу", contractForGeneration.DocumentSeria == null ? "" : contractForGeneration.DocumentSeria.ToString()},
                {"Номер документу", contractForGeneration.DocumentNumber == null ? "" : contractForGeneration.DocumentNumber.ToString()},
                {"ДВ", contractForGeneration.DateOfIssued == null ? "" : contractForGeneration.DateOfIssued.Day < 10 ? "0" + contractForGeneration.DateOfIssued.Day.ToString() : contractForGeneration.DateOfIssued.Day.ToString()},
                {"МВ", contractForGeneration.DateOfIssued == null ? "" : contractForGeneration.DateOfIssued.Month < 10 ? "0" + contractForGeneration.DateOfIssued.Month.ToString() : contractForGeneration.DateOfIssued.Month.ToString()},
                {"РВ", contractForGeneration.DateOfIssued == null ? "" : contractForGeneration.DateOfIssued.Year.ToString()},
                {"Орган, що видав документ", contractForGeneration.IssuedBy == null ? "" : contractForGeneration.IssuedBy.ToString()},
                {"Тип ТЗ", contractForGeneration.CarSubType.ToString()},
                {"Номер ТЗ", contractForGeneration.RegistrationNumber.ToString()},
                {"Марка ТЗ", contractForGeneration.Mark.ToString()},
                {"Модель ТЗ", contractForGeneration.Model.ToString()},
                {"Рік випуску", contractForGeneration.Year.ToString()},
                {"ВІН код", contractForGeneration.VinCode.ToString()},
                {"Місце реєстрації ТЗ", contractForGeneration.PlaceOfRegistration.ToString()},
                {"Таксі?", contractForGeneration.IsTaxi ? "Так" : "Ні"},
                {"ОТК?", contractForGeneration.IsOTK ? "Так" : "Ні"},
                {"Будь-хто?", "Так"},
                {"Місяці використання", "x"},//add function which will be return needed count of 'x'
                {"Дата наступного ОТК", "x"},//add function which will be calculate date next otk-------------------------------------------------
                {"ДС", DateTime.Now.Day < 10 ? "0" + DateTime.Now.Day.ToString() : DateTime.Now.Day.ToString()},
                {"МС", DateTime.Now.Month < 10 ? "0" + DateTime.Now.Month.ToString() : DateTime.Now.Month.ToString()},
                {"РС", Convert.ToString(DateTime.Now.Year.ToString()[2]) + Convert.ToString(DateTime.Now.Year.ToString()[3])},
                {"ДУ", DateTime.Now.Day < 10 ? "0" + DateTime.Now.Day.ToString() : DateTime.Now.Day.ToString()},
                {"МУ", DateTime.Now.Month < 10 ? "0" + DateTime.Now.Month.ToString() : DateTime.Now.Month.ToString()},
                {"РУ", Convert.ToString(DateTime.Now.Year.ToString()[2]) + Convert.ToString(DateTime.Now.Year.ToString()[3])},
                {"ПІБ", contractForGeneration.ClientSurname.ToString() + ' ' + contractForGeneration.ClientName[0].ToString().ToUpper() + '.' + contractForGeneration.ClientFathername[0].ToString().ToUpper() + '.'}
            };

            var engine = new Engine();
            string serverPath = Server.MapPath("~/Content/contracts/");
            string startPath = Path.Combine(serverPath, "template.docx");
            string finalPath = Path.Combine(serverPath, "templateOut.docx");

            engine.Merge(startPath, fieldValues, finalPath);

            return "Content/contracts/templateOut.docx";
        }

        private class DataWithWarningToSend
        {
            public List<CompanyToSend> CompaniesToSend { get; set; }
            public List<ResponseToClient> ErrorMessagesToClients { get; set; }
        }


        public class CompanyFeature
        {
            public string Title { get; set; }
            public string IconPath { get; set; }
        }

        public class CoefToSend
        {
            public string Name { get; set; }
            public double Value { get; set; }
        }

        public class CompanyToSend
        {
            public string CompanyName { get; set; }
            public int IdCompanyMiddleman { get; set; }
            public int CompanyRate { get; set; }
            public List<CompanyFeature> CompanyFeatures { get; set; }
            public double Franchise { get; set; }
            public double FullPrice { get; set; }
            public double DiscountPrice { get; set; }
            public string ImgPath { get; set; }
            public string ImageSize { get; set; }
            public string PageCompanyInfoPath { get; set; }
            public List<CoefToSend> Coefs { get; set; }
        }

        private dynamic GetPotsRequestBody()
        {
            System.IO.Stream request = Request.InputStream;
            request.Seek(0, SeekOrigin.Begin);
            string bodyData = new StreamReader(request).ReadToEnd();
            return JsonConvert.DeserializeObject(bodyData);
        }

        private class ContractForGeneration
        {
            public DateTime TermFrom { get; set; }
            public DateTime TermTo { get; set; }
            public double Franchise { get; set; }
            public string CompanyCode { get; set; }
            public string ClientName { get; set; }
            public string ClientSurname { get; set; }
            public string ClientFathername { get; set; }
            public string Address { get; set; }
            public string PersonalCode { get; set; }
            public string EDRPOU { get; set; }
            public string Phone { get; set; }
            public DateTime DateOfBirth { get; set; }
            public string DocumentType { get; set; }
            public string DocumentSeria { get; set; }
            public string DocumentNumber { get; set; }
            public DateTime DateOfIssued { get; set; }
            public string IssuedBy { get; set; }
            public string PlaceOfRegistration { get; set; }
            public bool IsTaxi { get; set; }
            public bool IsOTK { get; set; }
            public string CarSubType { get; set; }
            public string RegistrationNumber { get; set; }
            public string Mark { get; set; }
            public string Model { get; set; }
            public string VinCode { get; set; }
            public int Year { get; set; }
            public List<CoefToSend> Coefs { get; set; }
            public double TotalPrice { get; set; }
            public DateTime DateOfPayment { get; set; }
            public DateTime DateOfTheContract { get; set; }
        }

        //private void GetPlaceOfRegistration(dynamic dataParsed, ref CityOfRegistration cityOfRegistration, ref CountryOfRegistration countryOfRegistration)
        private InsuranceZoneOfRegistration GetZoneOfRegistration(dynamic dataParsed)
        {
            if (dataParsed.place.placeType == "code")
            {
                int currentCode = Convert.ToInt32(dataParsed.place.code.codeNum);
                TSC tsc = db.TSC.Where(t => t.Code == currentCode).First();
                //cityOfRegistration = tsc.CityOfRegistration;
                return tsc.CityOfRegistration.InsuranceZoneOfRegistration;
            }
            if (dataParsed.place.placeType == "address")
            {
                if (dataParsed.place.foreign == "false")
                {
                    string currentCity = Convert.ToString(dataParsed.place.city);
                    //cityOfRegistration = db.CityOfRegistration.Where(c => c.Name == currentCity).First();
                    return db.CityOfRegistration.Where(c => c.Name == currentCity).Select(c => c.InsuranceZoneOfRegistration).First();
                }
                else
                {
                    string currentCountry = Convert.ToString(dataParsed.place.foreign);
                    //countryOfRegistration = db.CountryOfRegistration.Where(c => c.Name == currentCountry).First();
                    return db.CountryOfRegistration.Where(c => c.Name == currentCountry).Select(c => c.InsuranceZoneOfRegistration).First();
                }
            }

            return null;
        }

        private string GetPlaceOfReg(dynamic dataParsed)
        {
            if (dataParsed.placeReg.placeType == "code")
            {
                int currentCode = Convert.ToInt32(dataParsed.placeReg.code.codeNum);
                TSC tsc = db.TSC.Where(t => t.Code == currentCode).First();
                return tsc.CityOfRegistration.Name;
            }
            if (dataParsed.placeReg.placeType == "address")
            {
                if (dataParsed.placeReg.foreign == "false")
                {
                    string currentCity = Convert.ToString(dataParsed.placeReg.city);
                    return currentCity;
                }
                else
                {
                    string currentCountry = Convert.ToString(dataParsed.placeReg.foreign);
                    return currentCountry;
                }
            }

            return "";
        }

        public class Transport
        {
            public CarGlobalType Type { get; set; }
            public CarInsuranceType SubType { get; set; }
        }

        //public class PlaceOfRegistration
        //{
        //    public CityOfRegistration CityOfRegistration { get; set; }
        //    public CountryOfRegistration CountryOfRegistration { get; set; }
        //}

        public class PeriodOfDocument
        {
            public double Period { get; set; }
            //Mandatory Technical Control (in rus OTK)
            public bool isMTC { get; set; }
        }

        public class DriverAccident
        {
            public bool IsDriverWasInAccident { get; set; }
            public int LastAccidentYear { get; set; }
        }

        public class ConditionsForDocument
        {
            public Transport Transport { get; set; }
            //public PlaceOfRegistration PlaceOfRegistration { get; set; }
            public InsuranceZoneOfRegistration InsuranceZoneOfRegistration { get; set; }
            public bool IsLegalEntity { get; set; }
            public PeriodOfDocument PeriodOfDocument { get; set; }
            public bool IsUseAsTaxi { get; set; }
            public DriverAccident DriverAccident { get; set; }
            public bool IsClientHasPrivilegs { get; set; }
            public int DocumentAdditionalLimits { get; set; }
        }

        private class TSCToSend
        {
            public int codeNum { get; set; }
            public string cityName { get; set; }
            public string regionName { get; set; }
        }
    }
}