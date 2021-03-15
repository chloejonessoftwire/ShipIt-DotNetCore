﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using ShipIt.Exceptions;
using ShipIt.Models.ApiModels;
using ShipIt.Models.DataModels;
using ShipIt.Repositories;



namespace ShipIt.Controllers
{
    [Route("orders/inbound")]
    public class InboundOrderController : ControllerBase
    {
        private static readonly log4net.ILog Log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod()?.DeclaringType);

        private readonly IEmployeeRepository _employeeRepository;
        private readonly ICompanyRepository _companyRepository;
        private readonly IProductRepository _productRepository;
        private readonly IStockRepository _stockRepository;

        public InboundOrderController(IEmployeeRepository employeeRepository, ICompanyRepository companyRepository, IProductRepository productRepository, IStockRepository stockRepository)
        {
            _employeeRepository = employeeRepository;
            _stockRepository = stockRepository;
            _companyRepository = companyRepository;
            _productRepository = productRepository;
        }

        [HttpGet("{warehouseId}")]
        public InboundOrderResponse Get([FromRoute] int warehouseId)
        {
            var functionStart = DateTime.Now;
            
            Log.Info("orderIn for warehouseId: " + warehouseId);

            var a = DateTime.Now;            
            var operationsManager = new Employee(_employeeRepository.GetOperationsManager(warehouseId));
            var b = DateTime.Now;
            var timeTaken = b-a;
            Console.WriteLine(timeTaken);

            Log.Debug(String.Format("Found operations manager: {0}", operationsManager));
            
            var stockListStart = DateTime.Now;
            var allStock = _stockRepository.GetRequiredStock(warehouseId);
            
            // var allStock = _stockRepository.GetStockByWarehouseId(warehouseId); 
            // var allStock = _stockRepository.GetRequiredStock(warehouseId);

            
            Dictionary<Company, List<InboundOrderLine>> orderlinesByCompany = new Dictionary<Company, List<InboundOrderLine>>();
            foreach (var stock in allStock)
            {
                // var productStart = DateTime.Now;
                // Product product = new Product(_productRepository.GetProductById(stock.ProductId));
                // ProductandCompany productcompany = new p
                // var productEnd = DateTime.Now;
                // var timeTakenProduct = productEnd - productStart; 
                // Console.WriteLine("Product Time Taken: "+ timeTakenProduct);
                
                // if(stock.held < product.LowerThreshold && !product.Discontinued)
                // {
                    // var companyStart = DateTime.Now;
                    Company company = new Company()
                    {
                        Gcp = stock.Gcp,
                        Name = stock.Name,
                        Addr2 = stock.Addr2,
                        Addr3 = stock.Addr3,
                        Addr4 = stock.Addr4,
                        PostalCode = stock.PostalCode,
                        City = stock.City,
                        Tel = stock.Tel,
                        Mail = stock.Mail
                    };
                    
                    // var companyEnd = DateTime.Now;
                    // var timeTakenCompany = companyEnd - companyStart; 
                    // Console.WriteLine("Company Time Taken: "+ timeTakenCompany);

                    var orderQuantity = Math.Max(stock.LowerThreshold * 3 - stock.held, stock.MinimumOrderQuantity);
                    var orderLinesStart = DateTime.Now;
                    if (!orderlinesByCompany.ContainsKey(company))
                    {
                        orderlinesByCompany.Add(company, new List<InboundOrderLine>());
                    }

                    orderlinesByCompany[company].Add( 
                        new InboundOrderLine()
                        {
                            gtin = stock.Gtin,
                            name = stock.Name,
                            quantity = orderQuantity
                        });
                    var orderLinesEnd = DateTime.Now;
                    var orderLineTime = orderLinesEnd - orderLinesStart;
                    // Console.WriteLine("Order Lines Time Taken : " + orderLineTime);

                // }
                
            }  
             var stockListEnd = DateTime.Now;
            var timeTakenStock = stockListEnd - stockListStart;
            Console.WriteLine("Stock List Time Taken: " + timeTakenStock);
            

            Log.Debug(String.Format("Constructed order lines: {0}", orderlinesByCompany));

            var orderSegments = orderlinesByCompany.Select(ol => new OrderSegment()
            {
                OrderLines = ol.Value,
                Company = ol.Key
            });

            Log.Info("Constructed inbound order");
            
           
            var functionEnd=DateTime.Now;
            var functionTimeTaken= functionEnd - functionStart;
            Console.WriteLine("Total time taken:" + functionTimeTaken );

            return new InboundOrderResponse()
            {
                OperationsManager = operationsManager,
                WarehouseId = warehouseId,
                OrderSegments = orderSegments
            };
            
        }

        [HttpPost("")]
        public void Post([FromBody] InboundManifestRequestModel requestModel)
        {
            Log.Info("Processing manifest: " + requestModel);

            var gtins = new List<string>();

            foreach (var orderLine in requestModel.OrderLines)
            {
                if (gtins.Contains(orderLine.gtin))
                {
                    throw new ValidationException(String.Format("Manifest contains duplicate product gtin: {0}", orderLine.gtin));
                }
                gtins.Add(orderLine.gtin);
            }

            IEnumerable<ProductDataModel> productDataModels = _productRepository.GetProductsByGtin(gtins);
            Dictionary<string, Product> products = productDataModels.ToDictionary(p => p.Gtin, p => new Product(p));

            Log.Debug(String.Format("Retrieved products to verify manifest: {0}", products));

            var lineItems = new List<StockAlteration>();
            var errors = new List<string>();

            foreach (var orderLine in requestModel.OrderLines)
            {
                if (!products.ContainsKey(orderLine.gtin))
                {
                    errors.Add(String.Format("Unknown product gtin: {0}", orderLine.gtin));
                    continue;
                }

                Product product = products[orderLine.gtin];
                if (!product.Gcp.Equals(requestModel.Gcp))
                {
                    errors.Add(String.Format("Manifest GCP ({0}) doesn't match Product GCP ({1})",
                        requestModel.Gcp, product.Gcp));
                }
                else
                {
                    lineItems.Add(new StockAlteration(product.Id, orderLine.quantity));
                }
            }

            if (errors.Count() > 0)
            {
                Log.Debug(String.Format("Found errors with inbound manifest: {0}", errors));
                throw new ValidationException(String.Format("Found inconsistencies in the inbound manifest: {0}", String.Join("; ", errors)));
            }

            Log.Debug(String.Format("Increasing stock levels with manifest: {0}", requestModel));
            _stockRepository.AddStock(requestModel.WarehouseId, lineItems);
            Log.Info("Stock levels increased");
        }
    }
}
