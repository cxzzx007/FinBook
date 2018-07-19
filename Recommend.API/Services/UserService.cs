﻿using BuildingBlocks.Resilience.Http;
using DnsClient;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Recommend.API.Dto;
using Newtonsoft.Json;

namespace Recommend.API.Services
{
    public class UserServcie: IUserService
    {
        private string userServiceUrl = "http://localhost:56688/";
        private IHttpClient _httpClient;
        private IDnsQuery _dns;
        private IOptions<ServiceDisvoveryOptions> _options;
        private ILogger<UserServcie> _logger;

        public UserServcie(IHttpClient httpClient,IDnsQuery dns, IOptions<ServiceDisvoveryOptions> options,ILogger<UserServcie> logger)
        {
            _dns = dns ?? throw new ArgumentNullException(nameof(dns));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<UserIdentity> GetBaseUserInfoAsync(int userId)
        {
            try
            {
                var result = await _dns.ResolveServiceAsync("service.consul", _options.Value.UserServiceName);

                var addressList = result.First().AddressList;
                var address = addressList.Any() ? addressList.First().ToString() : result.First().HostName.TrimEnd('.');
                var port = result.First().Port;
                userServiceUrl = $"http://{address}:{port}/";
                var response = await _httpClient.GetStringAsync(userServiceUrl + "api/user/baseinfo/"+userId);
                if (!string.IsNullOrEmpty(response))
                {
                    var userInfo = JsonConvert.DeserializeObject<UserIdentity>(response);
                    _logger.LogTrace($"Completed GetBaseUserInfoAsync with userId :{userId}");
                    return userInfo;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("GetBaseUserInfoAsync 在重试之后失败。。。。。" + ex.Message);
                throw ex;
            }
            return null;
        }
    }
}
