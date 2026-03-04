import { createRouter, createWebHistory } from 'vue-router'
import AgentsView from '../views/AgentsView.vue'
import TasksView from '../views/TasksView.vue'
import HistoryView from '../views/HistoryView.vue'
import PrioritiesView from '../views/PrioritiesView.vue'

const routes = [
  { path: '/', component: AgentsView },
  { path: '/tasks', component: TasksView },
  { path: '/priorities', component: PrioritiesView },
  { path: '/history', component: HistoryView },
]

export default createRouter({
  history: createWebHistory(),
  routes,
})
