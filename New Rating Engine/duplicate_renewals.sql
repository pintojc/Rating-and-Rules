select i_policy_id, count(*) count
from t_policy_transaction
where s_policy_transaction_name = 'Issue Renew'
group by 
s_policy_transaction_name,i_policy_id
having count(*) > 1