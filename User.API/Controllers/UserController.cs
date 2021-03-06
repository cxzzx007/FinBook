﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DotNetCore.CAP;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using User.API.Data;
using User.API.Dto;
using User.API.Events;
using User.API.Model;

namespace User.API.Controllers
{
    [Route("api/[controller]")]

    public class UserController : BaseController
    {
        private AppUserDbContext _userContext;
        private ILogger<UserController> _logger;
        private readonly ICapPublisher _publisher;
        public UserController(AppUserDbContext userContext, ILogger<UserController> logger, ICapPublisher publisher)
        {
            _userContext = userContext;
            _logger = logger;
            _publisher = publisher;
        }

        private void RaiseUserprofileChangedEvent(Model.AppUser user)
        {
            if (_userContext.Entry(user).Property(nameof(user.Name)).IsModified ||
                _userContext.Entry(user).Property(nameof(user.Avatar)).IsModified ||
                _userContext.Entry(user).Property(nameof(user.Company)).IsModified ||
                _userContext.Entry(user).Property(nameof(user.Title)).IsModified)
            {
                _publisher.Publish("finbook_userapi_userprofilechanged", new UserProfileChangedEvent {
                    Name= user.Name,
                    Avatar = user.Avatar,
                    Company = user.Company,
                    Title = user.Title,
                    UserId = user.Id,

                });
            }
        }

        // GET api/user/CheckOrCreate
        [Route("check-or-create")]
        [HttpPost]
        public async Task<IActionResult> CheckOrCreate(string phone)
        {
            var user =_userContext.Users.SingleOrDefault(u => u.Phone == phone);
            if (user == null)
            {
                //加入
                user = new AppUser { Phone = phone };
                _userContext.Users.Add(user);
                await _userContext.SaveChangesAsync();
            }
            return Ok(new { UserId = user.Id, user.Name, user.Company, user.Title, user.Avatar });
        }
        /// <summary>
        /// 获取当前用户信息
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("")]
        public async Task<IActionResult> Get()
        {
            var user = _userContext.Users.AsTracking()
                .Include(u => u.properties)//关联取出 UserProperty列表 
                .SingleOrDefaultAsync(u => u.Id == UserIdentity.UserId);
            //（使用当前用户的id）获取当前用户，一般非用户界面的获取，而是其他代码的获取，不能获取到时 需要异常处理
            if (user == null)
            {
                _logger.LogError($"错误的用户上下文id{UserIdentity.UserId}");
                throw new Exceptions.UserOperationException($"错误的用户上下文id{UserIdentity.UserId}");
            }
            return Json(user);
        }

        /// <summary>
        /// 获取指定userId的用户信息
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("baseinfo/{userId}")]
        public async Task<IActionResult> GetBaseUserInfo(int userId)
        {
            var user = await _userContext.Users.AsTracking()
                .SingleOrDefaultAsync(u => u.Id == userId);
            //（使用当前用户的id）获取当前用户，一般非用户界面的获取，而是其他代码的获取，不能获取到时 需要异常处理
            if (user == null)
            {
                _logger.LogError($"GetBaseUserInfo 没有获取到id{UserIdentity.UserId}的信息");
            }
            UserIdentity userIdentity = new UserIdentity
            {
                Avatar = user.Avatar,
                Company = user.Company,
                Name = user.Name,
                Title = user.Title,
                UserId = user.Id
            };
            return Json(userIdentity);
        }


        [Route("")]
        [HttpPatch]
        public async Task<IActionResult> Patch([FromBody]JsonPatchDocument<AppUser> appUserpatch )
        {
            //TBD handle users.Properties case  见 视屏任务 中 用户api 的相关小节
            //TBD 记录 ef core sql日志   resource :https://docs.microsoft.com/en-us/ef/core/miscellaneous/logging

            var user = await _userContext.Users
                .SingleOrDefaultAsync(u => u.Id == UserIdentity.UserId);
            appUserpatch.ApplyTo(user);

            using (var transaction = _userContext.Database.BeginTransaction())
            {
                try
                {
                    RaiseUserprofileChangedEvent(user);
                    await _userContext.SaveChangesAsync();
                    transaction.Commit();
                }
                catch(Exception ex)
                {
                    _logger.LogError($"更新用户信息失败：{ex.Message}");
                    transaction.Rollback();
                }
            }

            return Json(user);
        }

        [HttpPost]
        [Route("tags")]
        public async Task<IActionResult> GetUserTags()
        {
            return Json(await _userContext.UserTags.Where(u => u.UserId == UserIdentity.UserId).ToListAsync());
        }
        /// <summary>
        /// 手机号可以查询，但给一天次数的限制
        /// userid 查找则需要限制，是 内部或外部api 查询时可以，外部api 比如 用户好友api
        /// </summary>
        /// <param name="phone"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("search/{phone}")]
        public async Task<IActionResult> Search(string phone)
        {
            return Ok(await _userContext.Users.Include(u => u.properties).SingleOrDefaultAsync(u => u.Id == UserIdentity.UserId));
        }

        //TBD  FromBody 的api 调用问题待解决
        [HttpPut]
        [Route("tags")]
        public async Task<IActionResult> UpdateUserTags([FromBody]List<string> tags)
        {
            var originTags = await _userContext.UserTags.Where(u => u.UserId == UserIdentity.UserId).ToListAsync();
            var newTags = tags.Except(originTags.Select(t => t.Tag));

            await _userContext.UserTags.AddRangeAsync(newTags.Select(t => new Model.UserTag
            {
                CreateTime = DateTime.Now,
                UserId = UserIdentity.UserId,
                Tag = t
            }));
            await _userContext.SaveChangesAsync();
            return Ok();
        }
    }
}
