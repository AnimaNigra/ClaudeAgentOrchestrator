import { createRouter, createWebHistory } from 'vue-router'
import AgentsView from '../views/AgentsView.vue'
import TasksView from '../views/TasksView.vue'
import HistoryView from '../views/HistoryView.vue'
import WorktreesView from '../views/WorktreesView.vue'
import PrioritiesView from '../views/PrioritiesView.vue'
import ReaderView from '../views/ReaderView.vue'
import ToolsView from '../views/ToolsView.vue'
import SettingsView from '../views/SettingsView.vue'

const routes = [
  { path: '/', component: AgentsView },
  { path: '/tasks', component: TasksView },
  { path: '/priorities', component: PrioritiesView },
  { path: '/history', component: HistoryView },
  { path: '/worktrees', component: WorktreesView },
  { path: '/reader', component: ReaderView },
  { path: '/tools/:tool?', component: ToolsView },
  { path: '/settings', component: SettingsView },
]

export default createRouter({
  history: createWebHistory(),
  routes,
})
