(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.TodoTasks = api;
})(globalThis, function () {
  'use strict';
  const clone = value => JSON.parse(JSON.stringify(value));
  const id = () => globalThis.crypto?.randomUUID?.() || 'task-' + Date.now() + '-' + Math.random().toString(36).slice(2);
  function title(value) {
    if (typeof value !== 'string' || !value.trim() || value.trim().length > 200) throw new Error('任务名称需为 1–200 个字符。');
    return value.trim();
  }
  function find(tasks, taskId) {
    const task = tasks.find(item => item.id === taskId);
    if (!task) throw new Error('任务不存在，请重新读取清单。');
    return task;
  }
  function validate(tasks) {
    const ids = new Set();
    tasks.forEach(task => {
      if (typeof task.id !== 'string' || !task.id || ids.has(task.id)) throw new Error('任务 ID 无效或重复。');
      ids.add(task.id); title(task.title);
      if (typeof task.done !== 'boolean' || (task.parentId !== null && typeof task.parentId !== 'string')) throw new Error('任务状态或父任务格式错误。');
    });
    tasks.forEach(task => {
      let parentId = task.parentId;
      const visited = new Set([task.id]);
      while (parentId !== null) {
        if (visited.has(parentId)) throw new Error('不能把任务移到自己或自己的子任务下。');
        visited.add(parentId); parentId = find(tasks, parentId).parentId;
      }
    });
    return tasks;
  }
  function reopen(tasks, parentId) {
    while (parentId !== null) {
      const parent = find(tasks, parentId); parent.done = false; parent.completedAt = 0; parentId = parent.parentId;
    }
  }
  function setDone(tasks, taskId, done, time) {
    const task = find(tasks, taskId);
    if (done && !task.done) task.completedAt = time;
    if (!done) task.completedAt = 0;
    task.done = done;
    if (done) tasks.filter(item => item.parentId === taskId).forEach(child => setDone(tasks, child.id, true, time));
    else reopen(tasks, task.parentId);
  }
  class TaskStore {
    constructor(tasks = [], options = {}) {
      this._tasks = validate(clone(tasks)).map(task => ({ ...task, title: title(task.title), collapsed: !!task.collapsed,
        completedAt: task.done && Number.isSafeInteger(task.completedAt) && task.completedAt > 0 ? task.completedAt : 0 }));
      this.revision = 0;
      this._now = options.now || Date.now;
      this._clock = this._tasks.reduce((latest, task) => Math.max(latest, task.completedAt), this._now());
      this._listeners = new Set();
    }
    list() { return clone(this._tasks); }
    snapshot() { return { revision: this.revision, tasks: this.list() }; }
    subscribe(listener) { this._listeners.add(listener); return () => this._listeners.delete(listener); }
    _commit(action, source, mutate) {
      const tasks = this.list(), result = mutate(tasks); validate(tasks);
      if (JSON.stringify(tasks) !== JSON.stringify(this._tasks)) {
        this._tasks = tasks; this.revision++;
        const event = { action, source: source || 'ui', revision: this.revision };
        this._listeners.forEach(listener => { try { listener(event); } catch (error) { globalThis.reportError?.(error); } });
      }
      return clone({ revision: this.revision, ...result });
    }
    create(args, source) {
      const task = { id: id(), title: title(args.title), parentId: args.parentId ?? null, done: false, completedAt: 0, collapsed: false };
      return this._commit('create_task', source, tasks => {
        if (task.parentId !== null) { find(tasks, task.parentId).collapsed = false; reopen(tasks, task.parentId); }
        tasks.push(task); return { task };
      });
    }
    update(taskId, changes, source) {
      return this._commit('update_task', source, tasks => {
        const task = find(tasks, taskId);
        if (Object.hasOwn(changes, 'title')) task.title = title(changes.title);
        if (Object.hasOwn(changes, 'parentId') && changes.parentId !== task.parentId) {
          task.parentId = changes.parentId;
          validate(tasks);
          if (task.parentId !== null) { find(tasks, task.parentId).collapsed = false; if (!task.done) reopen(tasks, task.parentId); }
        }
        return { task };
      });
    }
    complete(taskId, done, source) {
      if (typeof done !== 'boolean') throw new Error('完成状态必须是 true 或 false。');
      const time = this._clock = Math.max(this._now(), this._clock + 1);
      return this._commit('set_task_completed', source, tasks => { setDone(tasks, taskId, done, time); return { task: find(tasks, taskId) }; });
    }
    collapse(taskId, collapsed, source) {
      return this._commit('collapse_task', source, tasks => { const task = find(tasks, taskId); task.collapsed = !!collapsed; return { task }; });
    }
    move(taskId, targetId, after = false, source) {
      if (typeof after !== 'boolean') throw new Error('排序位置参数必须是 true 或 false。');
      return this._commit('move_task', source, tasks => {
        const task = find(tasks, taskId), target = find(tasks, targetId);
        if (taskId === targetId || task.parentId !== target.parentId || task.done || target.done) throw new Error('只能调整同一父任务下的未完成任务顺序。');
        tasks.splice(tasks.indexOf(task), 1); tasks.splice(tasks.indexOf(target) + (after ? 1 : 0), 0, task);
        return { task };
      });
    }
    remove(taskId, source) {
      return this._commit('delete_task', source, tasks => {
        find(tasks, taskId); const ids = new Set([taskId]);
        let count; do { count = ids.size; tasks.forEach(task => { if (ids.has(task.parentId)) ids.add(task.id); }); } while (count !== ids.size);
        const removed = tasks.filter(task => ids.has(task.id));
        tasks.splice(0, tasks.length, ...tasks.filter(task => !ids.has(task.id))); return { removed };
      });
    }
    restore(snapshot, expectedRevision, source = 'undo') {
      if (this.revision !== expectedRevision) throw new Error('清单已有后续修改，无法撤销这次调整。');
      const restored = validate(clone(snapshot.tasks));
      return this._commit('restore_tasks', source, tasks => { tasks.splice(0, tasks.length, ...restored); return {}; });
    }
    execute(name, args = {}, source = 'api') {
      if (!args || typeof args !== 'object' || Array.isArray(args)) throw new Error('任务操作参数必须是对象。');
      const fields = { list_tasks: [], create_task: ['title', 'parentId'], update_task: ['id', 'title', 'parentId'], set_task_completed: ['id', 'completed'], move_task: ['id', 'targetId', 'after'], delete_task: ['id'] };
      if (fields[name] && Object.keys(args).some(key => !fields[name].includes(key))) throw new Error('任务操作包含未开放的参数。');
      switch (name) {
        case 'list_tasks': return this.snapshot();
        case 'create_task': return this.create(args, source);
        case 'update_task': return this.update(args.id, args, source);
        case 'set_task_completed': return this.complete(args.id, args.completed, source);
        case 'move_task': return this.move(args.id, args.targetId, args.after ?? false, source);
        case 'delete_task': return this.remove(args.id, source);
        default: throw new Error('不支持的任务操作：' + name);
      }
    }
  }
  const str = description => ({ type: 'string', description });
  const tool = (name, description, properties, required) => ({ type: 'function', function: {
    name, description, parameters: { type: 'object', properties, required, additionalProperties: false }
  } });
  const tools = [
    tool('list_tasks', '读取所有任务及父子关系。使用真实任务 ID，不能自行猜测 ID。', {}, []),
    tool('create_task', '创建主任务或子任务，返回新任务 ID。分步骤建立子任务时先创建父任务。', { title: str('清晰、可执行的任务名称，最多 200 字'), parentId: { type: ['string', 'null'], description: '父任务 ID；主任务为 null' } }, ['title']),
    tool('update_task', '修改任务名称或父任务。不能形成循环。', { id: str('任务 ID'), title: str('新的任务名称'), parentId: { type: ['string', 'null'], description: '新父任务 ID；移到顶层用 null' } }, ['id']),
    tool('set_task_completed', '设置完成状态。完成父任务会自动完成全部后代；取消子任务完成会重新打开祖先任务。', { id: str('任务 ID'), completed: { type: 'boolean' } }, ['id', 'completed']),
    tool('move_task', '调整同层级未完成任务的顺序。已完成任务按完成时间自动排序。', { id: str('需要移动的任务 ID'), targetId: str('同层级的参照任务 ID'), after: { type: 'boolean', description: 'true 为放到参照任务之后，否则放到之前' } }, ['id', 'targetId']),
    tool('delete_task', '删除指定任务及其全部子任务。仅在用户明确要求删除时使用。', { id: str('任务 ID') }, ['id'])
  ];
  return { TaskStore, tools, version: '1.0' };
});
