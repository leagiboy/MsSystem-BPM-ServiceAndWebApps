﻿using DotNetCore.CAP;
using JadeFramework.Core.Domain.Entities;
using JadeFramework.Core.Extensions;
using JadeFramework.WorkFlow;
using MsSystem.WF.IRepository;
using MsSystem.WF.IService;
using MsSystem.WF.Model;
using MsSystem.WF.ViewModel;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace MsSystem.WF.Service
{
    public class WorkFlowInstanceService : IWorkFlowInstanceService
    {
        private readonly IWFDatabaseFixture databaseFixture;
        private readonly IConfigService configService;
        private readonly ICapPublisher capPublisher;

        public WorkFlowInstanceService(IWFDatabaseFixture databaseFixture, IConfigService configService,ICapPublisher capPublisher)
        {
            this.databaseFixture = databaseFixture;
            this.configService = configService;
            this.capPublisher = capPublisher;
        }


        /// <summary>
        /// 获取当前节点执行人
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public string GetMakerList(FlowNode node)
        {
            if (node.SetInfo == null)
            {
                return MakerListEnum.None.ToString();
            }
            switch (node.SetInfo.NodeDesignate)
            {
                case FlowNodeSetInfo.ALL_USER:
                    return MakerListEnum.AllUser.ToString();
                case FlowNodeSetInfo.SPECIAL_USER:
                    return string.Join(",", node.SetInfo.Nodedesignatedata.Users);
                case FlowNodeSetInfo.SPECIAL_ROLE:
                    return string.Join(",", node.SetInfo.Nodedesignatedata.Roles);
                default:
                    return MakerListEnum.None.ToString();
            }
        }

        /// <summary>
        /// 获取权限系统Maker User Id
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public async Task<MakerListModel> GetSysMakerList(FlowNode node)
        {
            if (node.SetInfo == null)
            {
                return new MakerListModel
                {
                    MakerType = MakerListEnum.None
                };
            }
            switch (node.SetInfo.NodeDesignate)
            {
                case FlowNodeSetInfo.ALL_USER:
                    return new MakerListModel
                    {
                        MakerType = MakerListEnum.AllUser
                    };
                case FlowNodeSetInfo.SPECIAL_USER:
                    return new MakerListModel
                    {
                        UserIds = node.SetInfo.Nodedesignatedata.Users.Select(x => Convert.ToInt64(x)).ToList(),
                        MakerType = MakerListEnum.Users
                    };
                case FlowNodeSetInfo.SPECIAL_ROLE:
                    var userids = await configService.GetUserIdsByRoleIdsAsync(node.SetInfo.Nodedesignatedata.Roles.Select(x => Convert.ToInt64(x)).ToList());
                    return new MakerListModel
                    {
                        UserIds = userids,
                        MakerType = MakerListEnum.Roles
                    };
                default:
                    return new MakerListModel
                    {
                        MakerType = MakerListEnum.None
                    };
            }
        }

        /// <summary>
        /// 我的待办
        /// </summary>
        /// <param name="searchDto"></param>
        /// <returns></returns>
        public async Task<Page<UserWorkFlowDto>> GetUserTodoListAsync(WorkFlowTodoSearchDto searchDto)
        {
            return await databaseFixture.Db.WorkflowInstance.GetUserTodoListAsync(searchDto);
        }

        /// <summary>
        /// 获取用户流程操作历史记录
        /// </summary>
        /// <param name="searchDto"></param>
        /// <returns></returns>
        public async Task<Page<WorkFlowOperationHistoryDto>> GetUserOperationHistoryAsync(WorkFlowOperationHistorySearchDto searchDto)
        {
            return await databaseFixture.Db.WorkflowInstance.GetUserOperationHistoryAsync(searchDto);
        }

        /// <summary>
        /// 获取用户发起的流程
        /// </summary>
        /// <param name="pageIndex"></param>
        /// <param name="pageSize"></param>
        /// <param name="userId"></param>
        /// <returns></returns>
        public async Task<Page<UserWorkFlowDto>> GetUserWorkFlowPageAsync(int pageIndex, int pageSize, string userId)
        {
            return await databaseFixture.Db.WorkflowInstance.GetUserWorkFlowPageAsync(pageIndex, pageSize, userId);
        }

        /// <summary>
        /// 获取我的审批历史记录
        /// </summary>
        /// <param name="pageIndex"></param>
        /// <param name="pageSize"></param>
        /// <param name="userId"></param>
        /// <returns></returns>
        public async Task<Page<UserWorkFlowDto>> GetMyApprovalHistoryAsync(int pageIndex, int pageSize, string userId)
        {
            return await databaseFixture.Db.WorkflowInstance.GetMyApprovalHistoryAsync(pageIndex, pageSize, userId);
        }



        /// <summary>
        /// CAP发布订阅
        /// </summary>
        /// <param name="statusChange"></param>
        /// <param name="flowStatus">要改变成的状态</param>
        /// <returns></returns>
        private async Task FlowStatusChangePublisher(WorkFlowStatusChange statusChange, WorkFlowStatus flowStatus)
        {
            if (statusChange != null)
            {
                statusChange.Status = flowStatus;
                statusChange.FlowTime = DateTime.Now.ToTimeStamp();
                await capPublisher.PublishAsync(statusChange.TargetName, statusChange);
            }
        }

        #region 流程过程流转处理

        /// <summary>
        /// 添加或修改自定义表单数据
        /// </summary>
        /// <param name="addProcess"></param>
        /// <returns></returns>
        public async Task<WorkFlowResult> AddOrUpdateCustomFlowFormAsync(WorkFlowProcess addProcess)
        {
            using (var tran = databaseFixture.Db.BeginTransaction())
            {
                try
                {
                    var dbflow = await databaseFixture.Db.Workflow.FindByIdAsync(addProcess.FlowId);
                    if (addProcess.InstanceId == default(Guid))
                    {
                        WfWorkflowInstance workflowInstance = new WfWorkflowInstance
                        {
                            InstanceId = Guid.NewGuid(),
                            FlowId = dbflow.FlowId,
                            Code = DateTime.Now.ToTimeStamp() + string.Empty.CreateNumberNonce(),
                            CreateUserId = addProcess.UserId,
                            CreateUserName = addProcess.UserName,
                            FlowContent = dbflow.FlowContent,
                            IsFinish = null,
                            Status = (int)WorkFlowStatus.UnSubmit
                        };
                        await databaseFixture.Db.WorkflowInstance.InsertAsync(workflowInstance, tran);
                        addProcess.InstanceId = workflowInstance.InstanceId;

                        //表单关联记录创建
                        var dbform = await databaseFixture.Db.WorkflowForm.FindByIdAsync(addProcess.FormId);
                        WfWorkflowInstanceForm instanceForm = new WfWorkflowInstanceForm
                        {
                            Id = Guid.NewGuid(),
                            CreateUserId = addProcess.UserId,
                            FormContent = dbform.Content,
                            FormData = addProcess.FormData,
                            InstanceId = addProcess.InstanceId,
                            FormId = dbform.FormId,
                            FormType = dbform.FormType,
                            FormUrl = null
                        };
                        await databaseFixture.Db.WorkflowInstanceForm.InsertAsync(instanceForm, tran);
                    }
                    else
                    {
                        //实例不再创建
                        //表单关联记录修改
                        var dbinstanceForm = await databaseFixture.Db.WorkflowInstanceForm.FindAsync(m => m.InstanceId == addProcess.InstanceId);
                        dbinstanceForm.FormData = addProcess.FormData;
                        await databaseFixture.Db.WorkflowInstanceForm.UpdateAsync(dbinstanceForm, tran);
                    }

                    tran.Commit();
                    return WorkFlowResult.Success("提交成功", data: addProcess.InstanceId);
                }
                catch (Exception ex)
                {
                    tran.Rollback();
                    return WorkFlowResult.Error("提交失败");
                }
            }
        }

        /// <summary>
        /// 创建实例
        /// 注意事项：
        /// 1、流程开始节点不可添加任何条件分支（不符合逻辑，故人为规定）,即开始节点之后必须只能有一个任务节点，否则整个逻辑就错误了
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public async Task<WorkFlowResult> CreateInstanceAsync(WorkFlowInstanceDto model)
        {
            using (var tran = databaseFixture.Db.BeginTransaction())
            {
                try
                {
                    var dbflow = await databaseFixture.Db.Workflow.FindByIdAsync(model.FlowId);
                    MsWorkFlowContext context = new MsWorkFlowContext(new WorkFlow
                    {
                        FlowId = dbflow.FlowId,
                        FlowJSON = dbflow.FlowContent,
                        ActivityNodeId = default(Guid)
                    });

                    #region 创建/修改实例
                    WfWorkflowInstance workflowInstance;
                    if (model.InstanceId == default(Guid))
                    {
                        workflowInstance = new WfWorkflowInstance
                        {
                            InstanceId = Guid.NewGuid(),
                            FlowId = context.WorkFlow.FlowId,
                            Code = DateTime.Now.ToTimeStamp() + string.Empty.CreateNumberNonce(),
                            ActivityId = context.WorkFlow.NextNodeId,
                            ActivityName = context.WorkFlow.NextNode.Name,
                            ActivityType = (int)context.WorkFlow.NextNodeType,
                            PreviousId = context.WorkFlow.ActivityNodeId,
                            MakerList = (this.GetMakerList(context.WorkFlow.Nodes[context.WorkFlow.NextNodeId]) + ",").Trim(),
                            CreateUserId = model.UserId,
                            CreateUserName = model.UserName,
                            FlowContent = dbflow.FlowContent,
                            IsFinish = context.WorkFlow.NextNodeType.ToIsFinish(),
                            Status = (int)WorkFlowStatus.Running
                        };
                        await databaseFixture.Db.WorkflowInstance.InsertAsync(workflowInstance, tran);
                    }
                    else
                    {
                        workflowInstance = await databaseFixture.Db.WorkflowInstance.FindByIdAsync(model.InstanceId);
                        workflowInstance.ActivityId = context.WorkFlow.NextNodeId;
                        workflowInstance.ActivityName = context.WorkFlow.NextNode.Name;
                        workflowInstance.ActivityType = (int)context.WorkFlow.NextNodeType;
                        workflowInstance.PreviousId = context.WorkFlow.ActivityNodeId;
                        workflowInstance.MakerList = (this.GetMakerList(context.WorkFlow.Nodes[context.WorkFlow.NextNodeId]) + ",").Trim();
                        workflowInstance.FlowContent = dbflow.FlowContent;
                        workflowInstance.IsFinish = context.WorkFlow.NextNodeType.ToIsFinish();
                        workflowInstance.Status = (int)WorkFlowStatus.Running;
                        await databaseFixture.Db.WorkflowInstance.UpdateAsync(workflowInstance, tran);
                    }


                    #endregion

                    #region 创建流程实例表单关联记录

                    var dbform = await databaseFixture.Db.WorkflowForm.FindByIdAsync(dbflow.FormId);
                    if ((WorkFlowFormType)dbform.FormType == WorkFlowFormType.System)
                    {
                        WfWorkflowInstanceForm instanceForm = new WfWorkflowInstanceForm
                        {
                            Id = Guid.NewGuid(),
                            CreateUserId = model.UserId,
                            FormContent = model.StatusChange.KeyValue,//保存对应表单主键
                            FormData = model.StatusChange.KeyValue,
                            InstanceId = workflowInstance.InstanceId,
                            FormId = dbform.FormId,
                            FormType = dbform.FormType,
                            FormUrl = dbform.FormUrl
                        };
                        await databaseFixture.Db.WorkflowInstanceForm.InsertAsync(instanceForm, tran);
                    }
                    else
                    {
                        //强制修改为null
                        model.StatusChange = null;
                    }

                    #endregion

                    #region 创建流程操作记录

                    WfWorkflowOperationHistory operationHistory = new WfWorkflowOperationHistory
                    {
                        OperationId = Guid.NewGuid(),
                        InstanceId = workflowInstance.InstanceId,
                        CreateUserId = model.UserId,
                        CreateUserName = model.UserName,
                        Content = "提交流程",
                        NodeName = context.WorkFlow.ActivityNode.Name,
                        NodeId = context.WorkFlow.ActivityNodeId,
                        TransitionType = (int)WorkFlowMenu.Submit
                    };
                    await databaseFixture.Db.WorkflowOperationHistory.InsertAsync(operationHistory, tran);
                    #endregion

                    #region 创建流程流转记录

                    WfWorkflowTransitionHistory transitionHistory = new WfWorkflowTransitionHistory
                    {
                        TransitionId = Guid.NewGuid(),
                        InstanceId = workflowInstance.InstanceId,
                        FromNodeId = context.WorkFlow.ActivityNodeId,
                        FromNodeType = (int)context.WorkFlow.ActivityNodeType,
                        FromNodName = context.WorkFlow.ActivityNode.Name,
                        ToNodeId = context.WorkFlow.NextNodeId,
                        ToNodeType = (int)context.WorkFlow.NextNodeType,
                        ToNodeName = context.WorkFlow.NextNode.Name,
                        CreateUserId = model.UserId,
                        CreateUserName = model.UserName,
                        TransitionState = (int)WorkFlowTransitionStateType.Normal,
                        IsFinish = context.WorkFlow.NextNodeType.ToIsFinish(),
                    };
                    await databaseFixture.Db.WorkflowTransitionHistory.InsertAsync(transitionHistory, tran);
                    #endregion

                    //改变表单状态
                    await FlowStatusChangePublisher(model.StatusChange, WorkFlowStatus.Running);

                    tran.Commit();
                    return WorkFlowResult.Success();
                }
                catch (Exception ex)
                {
                    tran.Rollback();
                    return WorkFlowResult.Error(ex.Message);
                }
            }
        }

        /// <summary>
        /// 获取工作流进程信息
        /// </summary>
        /// <param name="process"></param>
        /// <returns></returns>
        public async Task<WorkFlowProcess> GetProcessAsync(WorkFlowProcess process)
        {
            WorkFlowProcess model = new WorkFlowProcess();
            model.InstanceId = process.InstanceId;
            var dbflow = await databaseFixture.Db.Workflow.FindByIdAsync(process.FlowId);
            model.FlowId = dbflow.FlowId;
            model.FlowName = dbflow.FlowName;
            model.FormId = dbflow.FormId;
            if (process.InstanceId == default(Guid))//流程刚开始
            {
                var dbform = await databaseFixture.Db.WorkflowForm.FindByIdAsync(dbflow.FormId);
                model.FormType = (WorkFlowFormType)dbform.FormType;
                model.FormContent = dbform.Content;
                model.FormUrl = dbform.FormUrl;
                model.FormData = null;
                model.Menus = new List<int>
                {
                    (int)WorkFlowMenu.Submit,
                    (int)WorkFlowMenu.FlowImage,
                    (int)WorkFlowMenu.Save,
                    (int)WorkFlowMenu.Return,
                };
            }
            else
            {
                var instanceform = await databaseFixture.Db.WorkflowInstanceForm.FindAsync(m => m.InstanceId == process.InstanceId);
                model.FormType = (WorkFlowFormType)instanceform.FormType;
                model.FormContent = instanceform.FormContent;
                model.FormUrl = instanceform.FormUrl;
                model.FormData = instanceform.FormData;

                var flowinstance = await databaseFixture.Db.WorkflowInstance.FindByIdAsync(process.InstanceId);
                if (flowinstance.IsFinish == null && model.FormType == WorkFlowFormType.Custom)//表示自定义表单刚保存情况
                {
                    model.Menus = new List<int>
                    {
                        (int)WorkFlowMenu.Submit,
                        (int)WorkFlowMenu.FlowImage,
                        (int)WorkFlowMenu.Save,
                        (int)WorkFlowMenu.Return
                    };
                    return model;
                }

                if (flowinstance.IsFinish == (int)WorkFlowInstanceStatus.IsFinish || flowinstance.IsFinish == (int)WorkFlowInstanceStatus.Deprecate)
                {
                    model.Menus = new List<int>
                    {
                        (int)WorkFlowMenu.Approval,
                        (int)WorkFlowMenu.FlowImage,
                        (int)WorkFlowMenu.Return,
                    };
                    return model;
                }
                //根据当前人获取可操作的按钮
                //获取下一步的执行人
                MsWorkFlowContext context = new MsWorkFlowContext(new WorkFlow
                {
                    FlowId = dbflow.FlowId,
                    FlowJSON = flowinstance.FlowContent,
                    ActivityNodeId = flowinstance.ActivityId
                });
                if (context.WorkFlow.ActivityNode.Type == FlowNode.START)//节点退回到开始节点情况
                {
                    var dbinstance = await databaseFixture.Db.WorkflowInstance.FindByIdAsync(process.InstanceId);
                    if (dbinstance.CreateUserId == process.UserId)
                    {
                        model.Menus = new List<int>
                        {
                            (int)WorkFlowMenu.ReSubmit,
                            (int)WorkFlowMenu.Approval,
                            (int)WorkFlowMenu.FlowImage,
                            (int)WorkFlowMenu.Save,
                            (int)WorkFlowMenu.Return,
                        };
                    }
                    else
                    {
                        model.Menus = new List<int>
                        {
                            (int)WorkFlowMenu.Approval,
                            (int)WorkFlowMenu.FlowImage,
                            (int)WorkFlowMenu.Return,
                        };
                    }
                    return model;
                }
                else
                {
                    var maker = await this.GetSysMakerList(context.WorkFlow.ActivityNode);
                    if (maker.MakerType != MakerListEnum.None)
                    {
                        if (maker.MakerType == MakerListEnum.Users)
                        {
                            var makerUsers = flowinstance.MakerList.Split(',').Where(m => !string.IsNullOrEmpty(m));
                            if (makerUsers.Contains(process.UserId))
                            {
                                model.Menus = new List<int>
                                {
                                    (int)WorkFlowMenu.Agree,
                                    (int)WorkFlowMenu.Deprecate,
                                    (int)WorkFlowMenu.Back,
                                };
                                //获取执行过的节点
                                var operationHis = await databaseFixture.Db.WorkflowOperationHistory.FindAllAsync(m => m.InstanceId == process.InstanceId);
                                model.ExecutedNode = operationHis.Where(m => m.TransitionType == (int)WorkFlowMenu.Agree || m.TransitionType == (int)WorkFlowMenu.Submit).Select(m => new FlowNode
                                {
                                    Id = m.NodeId,
                                    Name = m.NodeName
                                }).ToList();
                            }
                        }
                        else
                        {
                            if (maker.UserIds.Contains(process.UserId.ToInt64()))
                            {
                                model.Menus = new List<int>
                                {
                                    (int)WorkFlowMenu.Agree,
                                    (int)WorkFlowMenu.Deprecate,
                                    (int)WorkFlowMenu.Back,
                                };
                                //获取执行过的节点
                                var operationHis = await databaseFixture.Db.WorkflowOperationHistory.FindAllAsync(m => m.InstanceId == process.InstanceId);
                                model.ExecutedNode = operationHis.Where(m=> m.TransitionType == (int)WorkFlowMenu.Agree || m.TransitionType == (int)WorkFlowMenu.Submit).Select(m => new FlowNode
                                {
                                    Id = m.NodeId,
                                    Name = m.NodeName
                                }).ToList();
                            }
                        }
                        if (model.Menus == null)
                        {
                            model.Menus = new List<int>();
                        }
                        //终止按钮显示判断
                        var prenode = context.GetLinesForFrom(flowinstance.ActivityId);
                        if (prenode.Count == 1)
                        {
                            var nodeType = context.GetNodeType(prenode[0].From);
                            if (nodeType == WorkFlowInstanceNodeType.BeginRound && process.UserId == dbflow.CreateUserId)
                            {
                                model.Menus.Add((int)WorkFlowMenu.Stop);
                            }
                        }
                        model.Menus.Add((int)WorkFlowMenu.Approval);
                        model.Menus.Add((int)WorkFlowMenu.FlowImage);
                        model.Menus.Add((int)WorkFlowMenu.Return);
                    }
                    else
                    {
                        throw new Exception("未找到任何可执行类型!请检查流程图");
                    }
                }
            }
            return model;
        }

        /// <summary>
        /// 系统定制流程获取
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public async Task<WorkFlowProcess> GetProcessForSystemAsync(SystemFlowDto model)
        {
            WorkFlowProcess process = new WorkFlowProcess
            {
                UserId = model.UserId
            };
            var dbflowform = await databaseFixture.Db.WorkflowForm.FindAsync(m => m.FormUrl == model.FormUrl);
            var dbflow = await databaseFixture.Db.Workflow.FindAsync(m => m.FormId == dbflowform.FormId);
            process.FlowId = dbflow.FlowId;
            process.FlowName = dbflow.FlowName;
            process.FormId = dbflow.FormId;
            if (model.PageId.IsNullOrEmpty())
            {
                process.InstanceId = default(Guid);
            }
            else
            {
                var instanceform = await databaseFixture.Db.WorkflowInstanceForm.FindAsync(m => m.FormId == dbflowform.FormId && m.FormContent == model.PageId);
                process.InstanceId = instanceform != null ? instanceform.InstanceId : default(Guid);
            }
            return await GetProcessAsync(process);
        }

        /// <summary>
        /// 流程过程流转处理
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public async Task<WorkFlowResult> ProcessTransitionFlowAsync(WorkFlowProcessTransition model)
        {
            WorkFlowResult result = new WorkFlowResult();
            switch (model.MenuType)
            {
                case WorkFlowMenu.Submit:
                    break;
                case WorkFlowMenu.ReSubmit:
                    result = await ProcessTransitionReSubmitAsync(model);
                    break;
                case WorkFlowMenu.Agree:
                    result = await ProcessTransitionAgreeAsync(model);
                    break;
                case WorkFlowMenu.Deprecate:
                    result = await ProcessTransitionDeprecateAsync(model);
                    break;
                case WorkFlowMenu.Back:
                    result = await ProcessTransitionBackAsync(model);
                    break;
                case WorkFlowMenu.Stop://刚开始提交，下一个节点未审批情况，流程发起人可以终止
                    result = await ProcessTransitionStopAsync(model);
                    break;
                case WorkFlowMenu.Cancel:
                    break;
                case WorkFlowMenu.Throgh:
                    break;
                case WorkFlowMenu.Assign:
                    break;
                case WorkFlowMenu.View:
                    break;
                case WorkFlowMenu.FlowImage:
                    break;
                case WorkFlowMenu.Approval:
                    break;
                case WorkFlowMenu.CC:
                    break;
                case WorkFlowMenu.Suspend:
                    break;
                case WorkFlowMenu.Resume:
                    break;
                case WorkFlowMenu.Save:
                case WorkFlowMenu.Return:
                default:
                    result = WorkFlowResult.Error("未找到匹配按钮！");
                    break;
            }
            return result;
        }

        /// <summary>
        /// 计算票数
        /// </summary>
        /// <param name="InstanceId"></param>
        /// <param name="nodeId"></param>
        /// <param name="node"></param>
        /// <param name="chatParallelCalcType"></param>
        /// <returns></returns>
        private async Task<WorkFlowInstanceStatus> CalcVotes(Guid InstanceId, Guid nodeId, FlowNode node, ChatParallelCalcType chatParallelCalcType)
        {
            var dboperhis = await databaseFixture.Db.WorkflowOperationHistory.FindAllAsync(m => m.InstanceId == InstanceId && m.NodeId == nodeId);
            bool result;
            switch (chatParallelCalcType)
            {
                case ChatParallelCalcType.MoreThenHalf:
                    result = dboperhis.Count(m => m.TransitionType == (int)WorkFlowMenu.Agree) > (dboperhis.Count() / 2);
                    break;
                case ChatParallelCalcType.OneHundredPercent:
                default:
                    result = dboperhis.Count(m => m.TransitionType == (int)WorkFlowMenu.Agree) == dboperhis.Count();
                    break;
            }
            if (node.NodeType() == WorkFlowInstanceNodeType.EndRound)
            {
                return result ? WorkFlowInstanceStatus.IsFinish : WorkFlowInstanceStatus.Deprecate;
            }
            else
            {
                return result ? WorkFlowInstanceStatus.Running : WorkFlowInstanceStatus.Deprecate;
            }
        }

        /// <summary>
        /// 会签节点逻辑
        /// </summary>
        /// <param name="tran"></param>
        /// <param name="context"></param>
        /// <param name="dbflowinstance"></param>
        /// <param name="model"></param>
        /// <param name="flowInstanceStatus"></param>
        /// <returns></returns>
        private async Task ChatLogic(IDbTransaction tran, MsWorkFlowContext context,WfWorkflowInstance dbflowinstance, WorkFlowProcessTransition model, WorkFlowInstanceStatus flowInstanceStatus)
        {
            //并行逻辑
            if (context.WorkFlow.ActivityNode.SetInfo.ChatData.ChatType == ChatType.Parallel)
            {
                var makerUsers = dbflowinstance.MakerList.Split(',').Where(m => !string.IsNullOrEmpty(m)).ToList();
                WfWorkflowTransitionHistory transitionHistory = new WfWorkflowTransitionHistory
                {
                    TransitionId = Guid.NewGuid(),
                    InstanceId = dbflowinstance.InstanceId,
                    CreateUserId = model.UserId,
                    CreateUserName = model.UserName,
                    TransitionState = (int)WorkFlowTransitionStateType.Normal,
                    IsFinish = (int)flowInstanceStatus,
                    FromNodeId = context.WorkFlow.ActivityNodeId,
                    FromNodName = context.WorkFlow.ActivityNode.Name,
                    FromNodeType = (int)context.WorkFlow.ActivityNodeType,
                    ToNodeId = context.WorkFlow.ActivityNodeId,
                    ToNodeName = context.WorkFlow.ActivityNode.Name,
                    ToNodeType = (int)context.WorkFlow.ActivityNodeType,
                };
                await databaseFixture.Db.WorkflowTransitionHistory.InsertAsync(transitionHistory, tran);
                if (makerUsers.Count == 1)//当前人是最后一人
                {
                    var line = context.WorkFlow.Lines[dbflowinstance.ActivityId][0];
                    var nextNode = context.WorkFlow.Nodes[line.To];

                    WfWorkflowTransitionHistory transitionHistoryEnd = new WfWorkflowTransitionHistory
                    {
                        TransitionId = Guid.NewGuid(),
                        InstanceId = dbflowinstance.InstanceId,
                        FromNodeId = context.WorkFlow.ActivityNodeId,
                        FromNodName = context.WorkFlow.ActivityNode.Name,
                        FromNodeType = (int)context.WorkFlow.ActivityNodeType,
                        ToNodeId = nextNode.Id,
                        ToNodeType = (int)nextNode.NodeType(),
                        ToNodeName = nextNode.Name,
                        TransitionState = (int)WorkFlowTransitionStateType.Normal,
                        IsFinish = nextNode.NodeType().ToIsFinish(),
                        CreateUserId = model.UserId,
                        CreateUserName = model.UserName
                    };
                    await databaseFixture.Db.WorkflowTransitionHistory.InsertAsync(transitionHistoryEnd, tran);

                    //修改流程实例
                    dbflowinstance.PreviousId = dbflowinstance.ActivityId;
                    dbflowinstance.ActivityId = nextNode.Id;
                    dbflowinstance.ActivityName = nextNode.Name;
                    dbflowinstance.ActivityType = (int)nextNode.NodeType();
                    dbflowinstance.MakerList = (nextNode.NodeType() == WorkFlowInstanceNodeType.EndRound ? "" : this.GetMakerList(nextNode) + ",").Trim();
                    //计算票数
                    var result = await CalcVotes(dbflowinstance.InstanceId, dbflowinstance.PreviousId, nextNode, context.WorkFlow.ActivityNode.SetInfo.ChatData.ParallelCalcType);
                    dbflowinstance.IsFinish = (int)result;
                    await databaseFixture.Db.WorkflowInstance.UpdateAsync(dbflowinstance, tran);
                }
                else
                {
                    makerUsers.Remove(model.UserId);
                    dbflowinstance.MakerList = string.Join(",", makerUsers) + ",";
                    await databaseFixture.Db.WorkflowInstance.UpdateAsync(dbflowinstance, tran);
                }
            }
            //串行逻辑
            else
            {
                var users = context.WorkFlow.ActivityNode.SetInfo.Nodedesignatedata.Users;
                int index = 0;
                for (int i = 0; i < users.Length; i++)
                {
                    if (users[i] == model.UserId)
                    {
                        index = i + 1;
                        break;
                    }
                }
                string nextUserId = users.Length == index ? "" : users[index];
                WfWorkflowTransitionHistory transitionHistory = new WfWorkflowTransitionHistory
                {
                    TransitionId = Guid.NewGuid(),
                    InstanceId = dbflowinstance.InstanceId,
                    CreateUserId = model.UserId,
                    CreateUserName = model.UserName,
                    TransitionState = (int)WorkFlowTransitionStateType.Normal,
                    IsFinish = (int)flowInstanceStatus,
                    FromNodeId = context.WorkFlow.ActivityNodeId,
                    FromNodName = context.WorkFlow.ActivityNode.Name,
                    FromNodeType = (int)context.WorkFlow.ActivityNodeType,
                    ToNodeId = context.WorkFlow.ActivityNodeId,
                    ToNodeName = context.WorkFlow.ActivityNode.Name,
                    ToNodeType = (int)context.WorkFlow.ActivityNodeType,
                };
                await databaseFixture.Db.WorkflowTransitionHistory.InsertAsync(transitionHistory, tran);
                if (users.Length == index)//最后一个人时候
                {
                    var line = context.WorkFlow.Lines[dbflowinstance.ActivityId][0];
                    var nextNode = context.WorkFlow.Nodes[line.To];
                    WfWorkflowTransitionHistory transitionHistoryEnd = new WfWorkflowTransitionHistory
                    {
                        TransitionId = Guid.NewGuid(),
                        InstanceId = dbflowinstance.InstanceId,
                        FromNodeId = context.WorkFlow.ActivityNodeId,
                        FromNodName = context.WorkFlow.ActivityNode.Name,
                        FromNodeType = (int)context.WorkFlow.ActivityNodeType,
                        ToNodeId = nextNode.Id,
                        ToNodeType = (int)nextNode.NodeType(),
                        ToNodeName = nextNode.Name,
                        TransitionState = (int)WorkFlowTransitionStateType.Normal,
                        IsFinish = nextNode.NodeType().ToIsFinish(),
                        CreateUserId = model.UserId,
                        CreateUserName = model.UserName
                    };
                    await databaseFixture.Db.WorkflowTransitionHistory.InsertAsync(transitionHistoryEnd, tran);
                    //修改流程实例
                    dbflowinstance.PreviousId = dbflowinstance.ActivityId;
                    dbflowinstance.ActivityId = nextNode.Id;
                    dbflowinstance.ActivityName = nextNode.Name;
                    dbflowinstance.ActivityType = (int)nextNode.NodeType();
                    dbflowinstance.MakerList = (nextNode.NodeType() == WorkFlowInstanceNodeType.EndRound ? "" : this.GetMakerList(nextNode) + ",").Trim();
                    //计算票数
                    var result = await CalcVotes(dbflowinstance.InstanceId, dbflowinstance.PreviousId, nextNode, context.WorkFlow.ActivityNode.SetInfo.ChatData.ParallelCalcType);
                    dbflowinstance.IsFinish = (int)result;
                    await databaseFixture.Db.WorkflowInstance.UpdateAsync(dbflowinstance, tran);
                }
            }
        }

        /// <summary>
        /// 下个节点是会签逻辑
        /// </summary>
        /// <param name="tran"></param>
        /// <param name="context"></param>
        /// <param name="dbflowinstance"></param>
        /// <param name="flowInstanceStatus"></param>
        /// <returns></returns>
        private async Task NextChatLogic(IDbTransaction tran, MsWorkFlowContext context, WfWorkflowInstance dbflowinstance, WorkFlowInstanceStatus flowInstanceStatus)
        {
            dbflowinstance.IsFinish = (int)flowInstanceStatus;
            if (context.WorkFlow.NextNode.SetInfo.ChatData.ChatType == ChatType.Parallel)
            {
                //并行会签
                dbflowinstance.MakerList = string.Join(",", context.WorkFlow.NextNode.SetInfo.Nodedesignatedata.Users) + ",";
            }
            else
            {
                //串行会签
                dbflowinstance.MakerList = context.WorkFlow.NextNode.SetInfo.Nodedesignatedata.Users[0] + ",";
            }
            dbflowinstance.PreviousId = dbflowinstance.ActivityId;
            dbflowinstance.ActivityId = context.WorkFlow.NextNodeId;
            dbflowinstance.ActivityName = context.WorkFlow.NextNode.Name;
            dbflowinstance.ActivityType = (int)context.WorkFlow.NextNodeType;
            await databaseFixture.Db.WorkflowInstance.UpdateAsync(dbflowinstance, tran);
        }

        /// <summary>
        /// 重新提交流程
        /// 实例只有一次
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        protected async Task<WorkFlowResult> ProcessTransitionReSubmitAsync(WorkFlowProcessTransition model)
        {
            using (var tran = databaseFixture.Db.BeginTransaction())
            {
                try
                {
                    var dbflow = await databaseFixture.Db.Workflow.FindByIdAsync(model.FlowId);
                    MsWorkFlowContext context = new MsWorkFlowContext(new WorkFlow
                    {
                        FlowId = dbflow.FlowId,
                        FlowJSON = dbflow.FlowContent,
                        ActivityNodeId = default(Guid)
                    });

                    #region 改变之前实例

                    var dbinstance = await databaseFixture.Db.WorkflowInstance.FindByIdAsync(model.InstanceId);
                    dbinstance.ActivityId = context.WorkFlow.NextNodeId;
                    dbinstance.ActivityName = context.WorkFlow.NextNode.Name;
                    dbinstance.ActivityType = (int)context.WorkFlow.NextNodeType;
                    dbinstance.PreviousId = context.WorkFlow.ActivityNodeId;
                    dbinstance.MakerList = (this.GetMakerList(context.WorkFlow.Nodes[context.WorkFlow.NextNodeId]) + ",").Trim();
                    dbinstance.IsFinish = context.WorkFlow.NextNodeType.ToIsFinish();
                    dbinstance.Status = (int)WorkFlowStatus.Running;
                    await databaseFixture.Db.WorkflowInstance.UpdateAsync(dbinstance, tran);

                    #endregion

                    #region 创建流程操作记录

                    WfWorkflowOperationHistory operationHistory = new WfWorkflowOperationHistory
                    {
                        OperationId = Guid.NewGuid(),
                        InstanceId = dbinstance.InstanceId,
                        CreateUserId = model.UserId,
                        CreateUserName = model.UserName,
                        Content = "流程重新提交",
                        NodeName = context.WorkFlow.ActivityNode.Name,
                        NodeId = context.WorkFlow.ActivityNodeId,
                        TransitionType = (int)WorkFlowMenu.Submit
                    };
                    await databaseFixture.Db.WorkflowOperationHistory.InsertAsync(operationHistory, tran);
                    #endregion

                    #region 创建流程流转记录

                    WfWorkflowTransitionHistory transitionHistory = new WfWorkflowTransitionHistory
                    {
                        TransitionId = Guid.NewGuid(),
                        InstanceId = dbinstance.InstanceId,
                        FromNodeId = context.WorkFlow.ActivityNodeId,
                        FromNodeType = (int)context.WorkFlow.ActivityNodeType,
                        FromNodName = context.WorkFlow.ActivityNode.Name,
                        ToNodeId = context.WorkFlow.NextNodeId,
                        ToNodeType = (int)context.WorkFlow.NextNodeType,
                        ToNodeName = context.WorkFlow.NextNode.Name,
                        CreateUserId = model.UserId,
                        CreateUserName = model.UserName,
                        TransitionState = (int)WorkFlowTransitionStateType.Normal,
                        IsFinish = context.WorkFlow.NextNodeType.ToIsFinish(),
                    };
                    await databaseFixture.Db.WorkflowTransitionHistory.InsertAsync(transitionHistory, tran);

                    #endregion

                    //改变表单状态
                    await FlowStatusChangePublisher(model.StatusChange, WorkFlowStatus.Running);

                    tran.Commit();
                    return WorkFlowResult.Success();
                }
                catch (Exception ex)
                {
                    tran.Rollback();
                    return WorkFlowResult.Error(ex.Message);
                }
            }
        }

        /// <summary>
        /// 同意
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        protected async Task<WorkFlowResult> ProcessTransitionAgreeAsync(WorkFlowProcessTransition model)
        {
            using (var tran = databaseFixture.Db.BeginTransaction())
            {
                try
                {
                    WorkFlowStatus publishFlowStatus = WorkFlowStatus.Running;
                    var dbflowinstance = await databaseFixture.Db.WorkflowInstance.FindByIdAsync(model.InstanceId);
                    if (dbflowinstance.IsFinish == (int)WorkFlowInstanceStatus.IsFinish || dbflowinstance.IsFinish == (int)WorkFlowInstanceStatus.Deprecate)
                    {
                        return WorkFlowResult.Error("该流程已经结束！");
                    }
                    MsWorkFlowContext context = new MsWorkFlowContext(new WorkFlow
                    {
                        FlowId = model.FlowId,
                        FlowJSON = dbflowinstance.FlowContent,
                        ActivityNodeId = dbflowinstance.ActivityId,
                        PreviousId = dbflowinstance.PreviousId
                    });

                    //当前节点是会签节点
                    if (context.WorkFlow.ActivityNode.NodeType() == WorkFlowInstanceNodeType.ChatNode)
                    {
                        await ChatLogic(tran, context, dbflowinstance, model, WorkFlowInstanceStatus.Running);
                    }
                    else
                    {
                        if (context.IsMultipleNextNode)
                        {
                            var nextLines = context.GetLinesForTo(context.WorkFlow.ActivityNodeId);
                            var firstLine = nextLines.First().SetInfo;
                            if (firstLine.LineType == FlowLineSetInfoType.System)//只能有两种结果true/false
                            {
                                var mylineids = nextLines.Select(n => n.SetInfo.LineId.Value).ToList();
                                var dbflowlines = await databaseFixture.Db.WorkflowLine.GetByIds(mylineids);
                                var reallydbflowline = dbflowlines.First(m => m.ExecuteSQL == "1");
                                //得到真正的同意线条
                                var reallyline = nextLines.First(m => m.SetInfo.LineId == reallydbflowline.Id);
                                //得到要执行的节点
                                FlowNode reallynode = context.WorkFlow.Nodes[reallyline.To];
                                dbflowinstance.ActivityId = reallynode.Id;
                                dbflowinstance.ActivityName = reallynode.Name;
                                dbflowinstance.ActivityType = (int)reallynode.NodeType();
                                dbflowinstance.MakerList = (reallynode.NodeType() == WorkFlowInstanceNodeType.EndRound ? "" : this.GetMakerList(reallynode) + ",").Trim();
                                dbflowinstance.IsFinish = reallynode.NodeType().ToIsFinish();
                                await databaseFixture.Db.WorkflowInstance.UpdateAsync(dbflowinstance, tran);

                                #region 添加流转记录

                                WfWorkflowTransitionHistory transitionHistory = new WfWorkflowTransitionHistory
                                {
                                    TransitionId = Guid.NewGuid(),
                                    InstanceId = dbflowinstance.InstanceId,
                                    FromNodeId = context.WorkFlow.ActivityNodeId,
                                    FromNodName = context.WorkFlow.ActivityNode.Name,
                                    FromNodeType = (int)context.WorkFlow.ActivityNodeType,
                                    ToNodeId = reallynode.Id,
                                    ToNodeType = (int)reallynode.NodeType(),
                                    ToNodeName = reallynode.Name,
                                    TransitionState = (int)WorkFlowTransitionStateType.Normal,
                                    IsFinish = reallynode.NodeType().ToIsFinish(),
                                    CreateUserId = model.UserId,
                                    CreateUserName = model.UserName
                                };
                                await databaseFixture.Db.WorkflowTransitionHistory.InsertAsync(transitionHistory, tran);

                                #endregion
                            }
                            else
                            {
                                throw new NotImplementedException("自定义还未实现！");
                            }

                        }
                        else
                        {
                            //下个节点是会签节点
                            if (context.WorkFlow.NextNode.NodeType() == WorkFlowInstanceNodeType.ChatNode)
                            {
                                await NextChatLogic(tran, context, dbflowinstance, WorkFlowInstanceStatus.Running);
                            }
                            else
                            {
                                //修改流程实例
                                dbflowinstance.PreviousId = dbflowinstance.ActivityId;
                                //判断线条是否有条件判断
                                var lines = context.GetAllLines().Where(m => m.From == dbflowinstance.ActivityId).ToList();
                                if (lines.Count == 1)
                                {
                                    //正常流转
                                    dbflowinstance.ActivityId = context.WorkFlow.NextNodeId;
                                    dbflowinstance.ActivityName = context.WorkFlow.NextNode.Name;
                                    dbflowinstance.ActivityType = (int)context.WorkFlow.NextNodeType;
                                    dbflowinstance.MakerList = (context.WorkFlow.NextNodeType == WorkFlowInstanceNodeType.EndRound ? "" : this.GetMakerList(context.WorkFlow.NextNode) + ",").Trim();
                                    dbflowinstance.IsFinish = context.WorkFlow.NextNodeType.ToIsFinish();
                                    dbflowinstance.Status = (int)WorkFlowStatus.Running;
                                    await databaseFixture.Db.WorkflowInstance.UpdateAsync(dbflowinstance, tran);

                                    //流程结束情况
                                    if ((int)WorkFlowInstanceStatus.IsFinish == dbflowinstance.IsFinish)
                                    {
                                        publishFlowStatus = WorkFlowStatus.IsFinish;
                                    }
                                }
                                else
                                {
                                    /*
                                     多于两条条件分支的时候判断
                                     注意：其实任何一个节点它都包含两条条件连线分支，即同意和不同意,那么当前的方法应判断三条以上时候
                                     */
                                    //条件判断流转
                                    //var line = lines.First();
                                    //var dblines = await databaseFixture.Db.WorkflowLine.GetGroupLinesByIdAsync(line.SetInfo.LineId.Value);

                                    //TODO 此处功能待完善，自定义表单功能过于复杂，条件分支目前只能支持 真/假 即要么通过（同意）要么不通过（不同意）,
                                }
                            }

                            #region 添加流转记录

                            WfWorkflowTransitionHistory transitionHistory = new WfWorkflowTransitionHistory
                            {
                                TransitionId = Guid.NewGuid(),
                                InstanceId = dbflowinstance.InstanceId,
                                FromNodeId = context.WorkFlow.ActivityNodeId,
                                FromNodName = context.WorkFlow.ActivityNode.Name,
                                FromNodeType = (int)context.WorkFlow.ActivityNodeType,
                                ToNodeId = context.WorkFlow.NextNodeId,
                                ToNodeType = (int)context.WorkFlow.NextNodeType,
                                ToNodeName = context.WorkFlow.NextNode.Name,
                                TransitionState = (int)WorkFlowTransitionStateType.Normal,
                                IsFinish = context.WorkFlow.NextNodeType.ToIsFinish(),
                                CreateUserId = model.UserId,
                                CreateUserName = model.UserName
                            };
                            await databaseFixture.Db.WorkflowTransitionHistory.InsertAsync(transitionHistory, tran);

                            #endregion
                        }
                    }

                    #region 添加操作记录

                    WfWorkflowOperationHistory operationHistory = new WfWorkflowOperationHistory
                    {
                        OperationId = Guid.NewGuid(),
                        InstanceId = dbflowinstance.InstanceId,
                        CreateUserId = model.UserId,
                        CreateUserName = model.UserName,
                        Content = model.ProcessContent,
                        NodeId = context.WorkFlow.ActivityNodeId,
                        NodeName = context.WorkFlow.ActivityNode.Name,
                        TransitionType = (int)WorkFlowMenu.Agree
                    };
                    await databaseFixture.Db.WorkflowOperationHistory.InsertAsync(operationHistory, tran);

                    #endregion

                    await FlowStatusChangePublisher(model.StatusChange, publishFlowStatus);

                    tran.Commit();

                    return WorkFlowResult.Success();
                }
                catch (Exception ex)
                {
                    tran.Rollback();
                    return WorkFlowResult.Error(ex.Message);
                }
            }
        }

        /// <summary>
        /// 不同意
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        protected async Task<WorkFlowResult> ProcessTransitionDeprecateAsync(WorkFlowProcessTransition model)
        {
            using (var tran = databaseFixture.Db.BeginTransaction())
            {
                try
                {
                    var dbflowinstance = await databaseFixture.Db.WorkflowInstance.FindByIdAsync(model.InstanceId);
                    if (dbflowinstance.IsFinish == (int)WorkFlowInstanceStatus.IsFinish || dbflowinstance.IsFinish == (int)WorkFlowInstanceStatus.Deprecate)
                    {
                        return WorkFlowResult.Error("该流程已经结束！");
                    }
                    MsWorkFlowContext context = new MsWorkFlowContext(new WorkFlow
                    {
                        FlowId = model.FlowId,
                        FlowJSON = dbflowinstance.FlowContent,
                        ActivityNodeId = dbflowinstance.ActivityId,
                        PreviousId = dbflowinstance.PreviousId
                    });
                    if (context.WorkFlow.ActivityNode.NodeType() == WorkFlowInstanceNodeType.ChatNode)
                    {
                        await ChatLogic(tran, context, dbflowinstance, model, WorkFlowInstanceStatus.Deprecate);
                    }
                    else
                    {
                        if (context.IsMultipleNextNode)
                        {
                            var nextLines = context.GetLinesForTo(context.WorkFlow.ActivityNodeId);
                            var firstLine = nextLines.First().SetInfo;
                            if (firstLine.LineType == FlowLineSetInfoType.System)//只能有两种结果true/false
                            {
                                var mylineids = nextLines.Select(n => n.SetInfo.LineId.Value).ToList();
                                var dbflowlines = await databaseFixture.Db.WorkflowLine.GetByIds(mylineids);
                                var reallydbflowline = dbflowlines.First(m => m.ExecuteSQL == "0");
                                //得到真正执行的线条
                                var reallyline = nextLines.First(m => m.SetInfo.LineId == reallydbflowline.Id);
                                //得到要执行的节点
                                FlowNode reallynode = context.WorkFlow.Nodes[reallyline.To];
                                dbflowinstance.ActivityId = reallynode.Id;
                                dbflowinstance.ActivityName = reallynode.Name;
                                dbflowinstance.ActivityType = (int)reallynode.NodeType();
                                dbflowinstance.MakerList = (reallynode.NodeType() == WorkFlowInstanceNodeType.EndRound ? "" : this.GetMakerList(reallynode) + ",").Trim();
                                dbflowinstance.IsFinish = reallynode.NodeType().ToIsFinish();
                                dbflowinstance.Status = (int)WorkFlowStatus.Deprecate;
                                await databaseFixture.Db.WorkflowInstance.UpdateAsync(dbflowinstance, tran);

                                #region 流转记录

                                WfWorkflowTransitionHistory transitionHistory = new WfWorkflowTransitionHistory
                                {
                                    TransitionId = Guid.NewGuid(),
                                    InstanceId = dbflowinstance.InstanceId,
                                    TransitionState = (int)WorkFlowTransitionStateType.Reject,
                                    IsFinish = (int)WorkFlowInstanceStatus.Deprecate,
                                    CreateUserId = model.UserId,
                                    CreateUserName = model.UserName,
                                    FromNodeId = context.WorkFlow.ActivityNodeId,
                                    FromNodName = context.WorkFlow.ActivityNode.Name,
                                    FromNodeType = (int)context.WorkFlow.ActivityNodeType,
                                    ToNodeId = reallynode.Id,
                                    ToNodeType = (int)reallynode.NodeType(),
                                    ToNodeName = reallynode.Name
                                };
                                await databaseFixture.Db.WorkflowTransitionHistory.InsertAsync(transitionHistory, tran);
                                #endregion
                            }
                            else
                            {
                                throw new NotImplementedException("自定义还未实现！");
                            }
                        }
                        else
                        {
                            if (context.WorkFlow.NextNode.NodeType() == WorkFlowInstanceNodeType.ChatNode)
                            {
                                await NextChatLogic(tran, context, dbflowinstance, WorkFlowInstanceStatus.Deprecate);
                            }
                            else
                            {
                                dbflowinstance.MakerList = "";
                                dbflowinstance.IsFinish = (int)WorkFlowInstanceStatus.Deprecate;
                                dbflowinstance.Status = (int)WorkFlowStatus.Deprecate;
                                dbflowinstance.PreviousId = dbflowinstance.ActivityId;
                                dbflowinstance.ActivityId = context.WorkFlow.NextNodeId;
                                await databaseFixture.Db.WorkflowInstance.UpdateAsync(dbflowinstance, tran);
                            }
                            #region 流转记录

                            WfWorkflowTransitionHistory transitionHistory = new WfWorkflowTransitionHistory
                            {
                                TransitionId = Guid.NewGuid(),
                                InstanceId = dbflowinstance.InstanceId,
                                TransitionState = (int)WorkFlowTransitionStateType.Reject,
                                IsFinish = (int)WorkFlowInstanceStatus.Deprecate,
                                CreateUserId = model.UserId,
                                CreateUserName = model.UserName,
                                FromNodeId = context.WorkFlow.ActivityNodeId,
                                FromNodName = context.WorkFlow.ActivityNode.Name,
                                FromNodeType = (int)context.WorkFlow.ActivityNodeType,
                                ToNodeId = context.WorkFlow.NextNodeId,
                                ToNodeType = (int)context.WorkFlow.NextNodeType,
                                ToNodeName = context.WorkFlow.NextNode.Name
                            };
                            await databaseFixture.Db.WorkflowTransitionHistory.InsertAsync(transitionHistory, tran);
                            #endregion
                        }

                    }

                    #region 操作历史

                    WfWorkflowOperationHistory operationHistory = new WfWorkflowOperationHistory
                    {
                        OperationId = Guid.NewGuid(),
                        InstanceId = dbflowinstance.InstanceId,
                        CreateUserId = model.UserId,
                        CreateUserName = model.UserName,
                        Content = model.ProcessContent,
                        NodeName = context.WorkFlow.ActivityNode.Name,
                        NodeId = context.WorkFlow.ActivityNodeId,
                        TransitionType = (int)WorkFlowMenu.Deprecate
                    };

                    await databaseFixture.Db.WorkflowOperationHistory.InsertAsync(operationHistory, tran);
                    #endregion

                    await FlowStatusChangePublisher(model.StatusChange, WorkFlowStatus.Deprecate);

                    tran.Commit();
                    return WorkFlowResult.Success();
                }
                catch (Exception ex)
                {
                    tran.Rollback();
                    return WorkFlowResult.Error(ex.Message);
                }
            }
        }

        /// <summary>
        /// 流程退回
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        protected async Task<WorkFlowResult> ProcessTransitionBackAsync(WorkFlowProcessTransition model)
        {
            using (var tran = databaseFixture.Db.BeginTransaction())
            {
                try
                {
                    var dbflowinstance = await databaseFixture.Db.WorkflowInstance.FindByIdAsync(model.InstanceId);
                    if (dbflowinstance.IsFinish == (int)WorkFlowInstanceStatus.IsFinish || dbflowinstance.IsFinish == (int)WorkFlowInstanceStatus.Deprecate)
                    {
                        return WorkFlowResult.Error("该流程已经结束！");
                    }
                    if (model.NodeRejectType == null)
                    {
                        throw new Exception("参数报错！");
                    }
                    MsWorkFlowContext context = new MsWorkFlowContext(new WorkFlow
                    {
                        FlowId = model.FlowId,
                        FlowJSON = dbflowinstance.FlowContent,
                        ActivityNodeId = dbflowinstance.ActivityId,
                        PreviousId = dbflowinstance.PreviousId
                    });
                    if (context.WorkFlow.ActivityNodeType == WorkFlowInstanceNodeType.Normal || context.WorkFlow.ActivityNodeType == WorkFlowInstanceNodeType.BeginRound)
                    {
                        Guid rejectNodeId = context.RejectNode(model.NodeRejectType.Value, model.RejectNodeId);
                        FlowNode rejectNode = context.WorkFlow.Nodes[rejectNodeId];

                        dbflowinstance.PreviousId = dbflowinstance.ActivityId;
                        dbflowinstance.ActivityId = rejectNodeId;
                        dbflowinstance.ActivityName = rejectNode.Name;
                        dbflowinstance.ActivityType = (int)rejectNode.NodeType();
                        if (rejectNode.NodeType() == WorkFlowInstanceNodeType.BeginRound)//开始节点时候
                        {
                            dbflowinstance.MakerList = dbflowinstance.CreateUserId + ",";
                        }
                        else
                        {
                            dbflowinstance.MakerList = (rejectNode.NodeType() == WorkFlowInstanceNodeType.EndRound ? "" : this.GetMakerList(rejectNode) + ",").Trim(); ;
                        }
                        dbflowinstance.IsFinish = rejectNode.NodeType() == WorkFlowInstanceNodeType.EndRound ? (int)WorkFlowInstanceStatus.IsFinish : (int)WorkFlowInstanceStatus.Running;
                        dbflowinstance.Status = (int)WorkFlowStatus.Back;
                        await databaseFixture.Db.WorkflowInstance.UpdateAsync(dbflowinstance, tran);

                        #region 流转记录

                        WfWorkflowTransitionHistory transitionHistory = new WfWorkflowTransitionHistory
                        {
                            TransitionId = Guid.NewGuid(),
                            InstanceId = dbflowinstance.InstanceId,
                            CreateUserId = model.UserId,
                            CreateUserName = model.UserName,
                            IsFinish = (int)WorkFlowInstanceStatus.Running,
                            TransitionState = (int)WorkFlowTransitionStateType.Reject,
                            FromNodeId = context.WorkFlow.ActivityNodeId,
                            FromNodeType = (int)context.WorkFlow.ActivityNodeType,
                            FromNodName = context.WorkFlow.ActivityNode.Name,
                            ToNodeId = rejectNodeId,
                            ToNodeType = (int)rejectNode.NodeType(),
                            ToNodeName = rejectNode.Name
                        };
                        await databaseFixture.Db.WorkflowTransitionHistory.InsertAsync(transitionHistory, tran);

                        #endregion

                        #region 操作记录

                        WfWorkflowOperationHistory operationHistory = new WfWorkflowOperationHistory
                        {
                            OperationId = Guid.NewGuid(),
                            InstanceId = dbflowinstance.InstanceId,
                            CreateUserId = model.UserId,
                            CreateUserName = model.UserName,
                            Content = model.ProcessContent,
                            NodeName = context.WorkFlow.ActivityNode.Name,
                            TransitionType = (int)WorkFlowMenu.Back,
                            NodeId = context.WorkFlow.ActivityNodeId
                        };
                        await databaseFixture.Db.WorkflowOperationHistory.InsertAsync(operationHistory, tran);
                        #endregion
                    }
                    else
                    {
                        return WorkFlowResult.Error("当前节点为会签节点，不可退回！");
                    }

                    await FlowStatusChangePublisher(model.StatusChange, WorkFlowStatus.Back);

                    tran.Commit();

                    return WorkFlowResult.Success();
                }
                catch (Exception ex)
                {
                    tran.Rollback();
                    return WorkFlowResult.Error(ex.Message);
                }
            }
        }

        /// <summary>
        /// 流程终止
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        protected async Task<WorkFlowResult> ProcessTransitionStopAsync(WorkFlowProcessTransition model)
        {
            using (var tran= databaseFixture.Db.BeginTransaction())
            {
                try
                {
                    var dbflow = await databaseFixture.Db.Workflow.FindByIdAsync(model.FlowId);
                    var dbflowinstance = await databaseFixture.Db.WorkflowInstance.FindByIdAsync(model.InstanceId);
                    var dbinstanceForm = await databaseFixture.Db.WorkflowInstanceForm.FindAsync(m => m.InstanceId == dbflowinstance.InstanceId);
                    if ((WorkFlowFormType)dbinstanceForm.FormType == WorkFlowFormType.System)//定制表单
                    {
                        //删除流程实例
                        await databaseFixture.Db.WorkflowInstance.DeleteAsync(dbflowinstance, tran);
                        //删除流程实例表单关联记录
                        await databaseFixture.Db.WorkflowInstanceForm.DeleteAsync(dbinstanceForm, tran);
                    }
                    else
                    {
                        //自定义表单流程实例修改
                        dbflowinstance.IsFinish = null;
                        dbflowinstance.Status = (int)WorkFlowStatus.UnSubmit;
                        await databaseFixture.Db.WorkflowInstance.UpdateAsync(dbflowinstance, tran);
                    }
                    //删除流程操作记录
                    var dboperationHistory = await databaseFixture.Db.WorkflowOperationHistory.FindAllAsync(m => m.InstanceId == model.InstanceId);
                    foreach (var item in dboperationHistory)
                    {
                        await databaseFixture.Db.WorkflowOperationHistory.DeleteAsync(item, tran);
                    }
                    //删除流程流转记录
                    var dbtransitionHistory = await databaseFixture.Db.WorkflowTransitionHistory.FindAllAsync(m => m.InstanceId == model.InstanceId);
                    foreach (var item in dbtransitionHistory)
                    {
                        await databaseFixture.Db.WorkflowTransitionHistory.DeleteAsync(item, tran);
                    }

                    tran.Commit();
                    return WorkFlowResult.Success();
                }
                catch (Exception ex)
                {
                    tran.Rollback();
                    return WorkFlowResult.Error("流程终止失败！");
                }
            }
        }

        /// <summary>
        /// 获取审批意见
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public async Task<WorkFlowResult> GetFlowApprovalAsync(WorkFlowProcessTransition model)
        {
            var dbhistory = await databaseFixture.Db.WorkflowOperationHistory.FindAllAsync(m => m.InstanceId == model.InstanceId);
            return WorkFlowResult.Success(string.Empty, dbhistory.OrderBy(m => m.CreateTime));
        }

        /// <summary>
        /// 获取流程图信息
        /// </summary>
        /// <param name="instanceId">实例ID</param>
        /// <returns></returns>
        public async Task<WorkFlowImageDto> GetFlowImageAsync(Guid flowid, Guid? instanceId)
        {
            if (instanceId == null || instanceId.Value == default(Guid))
            {
                var dbflow = await databaseFixture.Db.Workflow.FindByIdAsync(flowid);
                return new WorkFlowImageDto
                {
                    FlowId = dbflow.FlowId,
                    FlowContent = dbflow.FlowContent,
                    InstanceId = default(Guid),
                    CurrentNodeId = default(Guid)
                };
            }
            else
            {
                var instance = await databaseFixture.Db.WorkflowInstance.FindAsync(m => m.InstanceId == instanceId);
                return new WorkFlowImageDto
                {
                    FlowId = instance.FlowId,
                    InstanceId = instance.InstanceId,
                    CurrentNodeId = instance.ActivityId,
                    FlowContent = instance.FlowContent,
                };
            }

        }

        #endregion

    }
}
