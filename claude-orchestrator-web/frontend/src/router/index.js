import { createRouter, createWebHistory } from 'vue-router'
import AgentsView from '../views/AgentsView.vue'
import TasksView from '../views/TasksView.vue'
import HistoryView from '../views/HistoryView.vue'

const routes = [
  { path: '/', component: AgentsView },
  { path: '/tasks', component: TasksView },
  { path: '/history', component: HistoryView },
]

export default createRouter({
  history: createWebHistory(),
  routes,
})
