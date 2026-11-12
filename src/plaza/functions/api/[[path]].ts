import { handleRequest, type Env } from '../../server/api'
export const onRequest: PagesFunction<Env> = ({ request, env }) => handleRequest(request, env)
